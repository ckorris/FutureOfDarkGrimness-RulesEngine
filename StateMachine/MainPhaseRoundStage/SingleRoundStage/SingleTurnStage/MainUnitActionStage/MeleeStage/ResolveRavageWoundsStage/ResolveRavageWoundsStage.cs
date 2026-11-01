using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P10 Ravage: when a unit reaches melee, each model carrying Ravage(X) rolls X dice and every 6+
    /// deals one DIRECT wound to the defender, resolved BEFORE the normal swings. The auto-wound sibling of
    /// <see cref="ResolveImpactHitsStage"/> (Impact) — same charge-contact trigger, same automatic pool
    /// roll — but the wounds take NO armor save (owner ruling 2026-07-22): they skip
    /// <c>DetermineSaveRollsNeededStage</c> and <c>RollToSaveStage</c> and enter
    /// <see cref="AssignWoundsStage{TMetadata}"/> directly, where Regeneration and Tough still apply.
    ///
    /// Fires at <see cref="EHookID.Melee_OnChargeContact"/> (melee is only ever entered via Charge, so this
    /// is "when it is this unit's turn to attack in melee"); the per-model dice count is folded in the
    /// <see cref="Effect.DealAutoWounds"/> effect (X x living carriers), and this stage sums the emitted
    /// <see cref="RuleOperation.InvokeDealAutoWounds"/> dice and rolls one pool.
    ///
    /// Ravage is worded "when attacking in melee", NOT "when charging" (that is Impact), so the stage is
    /// wired ONCE PER SWINGING UNIT rather than once per melee: <see cref="MeleeStage"/> runs it after
    /// <see cref="DetermineStrikeOrderStage"/> — so a Counter/Unwieldy swap puts the CHARGED unit in the
    /// Actor seat, which is who is about to swing — and <see cref="StrikeBackStage"/> runs a twin on its
    /// own reversed context. Every combatant that swings therefore rolls its Ravage exactly once,
    /// immediately before its own swings, and neither placement can double-roll the same unit.
    ///
    /// #434 (OPR 3.6): "Once per round, when it's this model's turn to strike in melee, roll X dice before attacking,
    /// unless this unit is Fatigued" - Ravage, and Regenerative Strength's token pool (4+) in the same words. Three
    /// changes follow. (1) Per model, by #447's template: the striker joins with its living models
    /// (<see cref="ResolveImpactHitsStage.ChargerParticipant"/>), so a joined hero's own Ravage (relocated onto its
    /// model by the #006 merge) fires, and <see cref="Effect.DealAutoWounds"/> counts the carriers of each
    /// attachment. (2) Once per round: a unit whose pool was rolled is stamped
    /// (<see cref="TokenType.StrikeDiceUsed"/>, round end) and skipped at its next strike this round - every rule
    /// this stage resolves says "once per round", so the gate lives here, once. (3) The Fatigue clause is each
    /// rule's own condition (<see cref="Condition.CountsAsFatiguedInMelee"/>). Each rule's pool is rolled and named
    /// on its own ("Ravage", "Regenerative Strength").
    ///
    /// Note this is a second live evaluation of <see cref="EHookID.Melee_OnChargeContact"/> in one melee,
    /// and <c>EvaluateAll</c> spends one-shot (NextTrigger) grants. Harmless for every rule at the hook
    /// today (Impact, Heavy Impact, Brace and Ravage are all permanent, ThisAttack entries),
    /// but a future NextTrigger grant of Impact would be consumed here without being rolled.
    /// </summary>
    public class ResolveRavageWoundsStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
        public StageBinding OnRavageResolved;

        // The auto-wound histogram rolled this melee (its TotalRolls is the fractional wound count),
        // computed in Enter and seeded into the child metadata. Only meaningful between Enter and the
        // child pipeline running.
        private IDiceResults? _woundDice;

        public ResolveRavageWoundsStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(ICombatActionContext context)
        {
            _woundDice = null;

            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            // #434: "once per round" - a unit that already rolled its pre-strike dice this round rolls none now.
            // Checked before the evaluation, so a skipped strike also spends no one-shot grant here.
            if (attacker.Tokens.HasToken(TokenType.StrikeDiceUsed))
            {
                GameContext.LogDebug($"{attacker.Name} already rolled its once-per-round strike dice this round.");
                await OnRavageResolved.Activate(context);
                return;
            }

            // ResolveImpactHitsStage's participant shape (#447): the striker joins with its living models, so a
            // joined hero's own copy fires beside the unit's, and the defender's living models ride the Subject seat
            // so the effect's EffectiveTarget resolves to the defender (and any future defensive auto-wound reducer
            // would fold in here).
            var participants = new List<RuleParticipant> { ResolveImpactHitsStage.ChargerParticipant(attacker) };
            participants.AddRange(DetermineStrikeOrderStage.SubjectWithMeleeWeapons(defender));

            // Narrate only the auto-wound ops: the striker's models also carry its Impact, which only the impact
            // stage resolves.
            IReadOnlyList<(RuleOperation Op, string RuleName)> named = GameContext.RuleEvaluator.EvaluateAllNamedLive(
                new ChargeContactContext(attacker, defender), op => op is RuleOperation.InvokeDealAutoWounds,
                participants.ToArray());

            var autoWounds = named
                .Where(n => n.Op is RuleOperation.InvokeDealAutoWounds { DiceCount: > 0 })
                .Select(n => (Op: (RuleOperation.InvokeDealAutoWounds)n.Op, Rule: RuleLabel(n.RuleName)))
                .ToList();
            if (autoWounds.Count == 0)
            {
                await OnRavageResolved.Activate(context);
                return;
            }

            attacker.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.StrikeDiceUsed));

            // One pool per rule (and threshold): Ravage at 6+, Regenerative Strength at 4+. Each pool presents its
            // own beat; the wound counts accumulate into one total the save-skipping pipeline consumes.
            float totalWounds = 0f;
            var described = new List<string>();
            foreach (var group in autoWounds.GroupBy(a => (a.Rule, a.Op.SuccessThreshold)))
            {
                int dice = group.Sum(a => a.Op.DiceCount);
                IDiceResults wounds = await SyntheticWoundResolution.RollWoundPool(
                    GameContext, dice, group.Key.SuccessThreshold, group.Key.Rule, defender.Name);
                totalWounds += wounds.TotalRolls;
                described.Add($"{group.Key.Rule} {dice} {(dice == 1 ? "die" : "dice")} at " +
                    $"{group.Key.SuccessThreshold}+");
            }

            GameContext.Log($"{attacker.Name}: {string.Join(", ", described)} dealt {totalWounds:0.##} unsaveable " +
                $"wound(s) to {defender.Name}.");

            if (totalWounds <= 0f)
            {
                await OnRavageResolved.Activate(context);
                return;
            }

            // Collapse to a single wound histogram (faces are cosmetic for auto-wounds - saves count by
            // total) and run the assign -> apply sub-pipeline.
            _woundDice = SyntheticHitResolution.SyntheticHits(totalWounds);
            await base.Enter(context);
        }

        /// <summary>The rule's name without an authored value ("Ravage(3)" -> "Ravage"): the beat and log name the
        /// rule, the dice count already carries X.</summary>
        private static string RuleLabel(string requestedName)
        {
            int paren = requestedName.IndexOf('(');
            return paren > 0 ? requestedName.Substring(0, paren).TrimEnd() : requestedName;
        }

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            // A weaponless auto-wound: model it as a synthetic AP-0 melee attack so AssignWoundsStage can
            // read it, but hand it PRE-FAILED saves so no armor save is rolled. The bare weapon carries no
            // rules, so no attacker-side fold (Deadly/Shred) fires - only the defender's Regeneration/Tough.
            Weapon ravageWeapon = new Weapon("Ravage", rangeInches: 0f, attacks: 0, armorPenetration: 0);

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.AttackingUnit,
                contextSelf.DefendingUnit, ravageWeapon, weaponCount: 1, isMelee: true);

            metadata.AddResult(SyntheticWoundResolution.AsUnsavedWounds(_woundDice!));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnRavageResolved = new StageBinding(this);

            // No DetermineSaveRollsNeeded / RollToSave: the wounds are unsaveable, so the pipeline starts at
            // wound assignment (where Regeneration and Tough live) and applies.
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnRavageResolved), OnRavageResolved, out string ravageResolvedEvent)
                .Build();

            startingChild = assignWounds;

            assignWounds.BindNextStage(applyWounds)
                .BindToEvent(ravageResolvedEvent);

            return dictionary;
        }
    }
}
