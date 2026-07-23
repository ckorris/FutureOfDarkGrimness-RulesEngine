using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
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
    /// DEFERRED: a Ravage unit that is CHARGED does not roll on its strike-back — only the charger triggers
    /// this stage, mirroring Impact's charge-only scope. Recorded in the #197 ledger, not silently dropped.
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

            // Same participant shape as ResolveImpactHitsStage: the attacker (Actor) emits its Ravage dice,
            // and the defender's living models ride the Subject seat so the effect's EffectiveTarget resolves
            // to the defender (and any future defensive auto-wound reducer would fold in here).
            var participants = new List<RuleParticipant> { RuleParticipant.Actor(attacker) };
            participants.AddRange(DetermineStrikeOrderStage.SubjectWithMeleeWeapons(defender));

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new ChargeContactContext(attacker, defender), participants.ToArray());

            List<RuleOperation.InvokeDealAutoWounds> autoWounds = operations
                .OfType<RuleOperation.InvokeDealAutoWounds>().ToList();
            if (autoWounds.Count == 0)
            {
                await OnRavageResolved.Activate(context);
                return;
            }

            // Ravage always rolls at 6+, but group by threshold so a future auto-wound rule at this hook
            // with a different threshold rolls its own pool. Each pool presents its own beat; the wound
            // counts accumulate into one total the save-skipping pipeline consumes.
            float totalWounds = 0f;
            foreach (IGrouping<int, RuleOperation.InvokeDealAutoWounds> group in
                     autoWounds.GroupBy(op => op.SuccessThreshold))
            {
                int dice = group.Sum(op => op.DiceCount);
                if (dice <= 0) continue;

                IDiceResults wounds = await SyntheticWoundResolution.RollWoundPool(
                    GameContext, dice, group.Key, "Ravage", defender.Name);
                totalWounds += wounds.TotalRolls;
            }

            GameContext.Log($"{attacker.Name}'s Ravage dealt {totalWounds:0.##} unsaveable wound(s) to {defender.Name}.");

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
