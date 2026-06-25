using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #033 — the Caster "Cast" action. Reached from <see cref="ChooseActionStage"/> when the activating
    /// unit carries Caster(X) and its army has an affordable spell. The player picks a spell from the army's
    /// list, picks target(s) within the spell's range / line of sight / affinity, spends the spell's token
    /// cost (paid on the *attempt*, whether or not it succeeds), and rolls one die — on a 4+ the spell is
    /// cast and its effect applied. Then it loops back to Choose Action via <see cref="OnFinished"/>,
    /// **layered** like a custom action (it never sets HasMoved/HasAttacked), so the unit may still
    /// Move/Shoot and may cast again while it can afford another spell.
    ///
    /// Two effect archetypes:
    /// <list type="bullet">
    ///   <item><b>Buff/debuff</b> (<see cref="Effect.AddRule"/> &amp; other token effects): the effect is
    ///         applied to each target via the polymorphic <see cref="Effect.Apply"/> and the resulting token
    ///         operations are committed — the "gets RULE once (next time)" shape.</item>
    ///   <item><b>Damage</b> (<see cref="Effect.DealHits"/>): <see cref="ResolveSpellHits"/> rolls the hits
    ///         as real dice and runs the hit-complete fold (Blast multiply, on-6 extra hits, Rending AP),
    ///         then the hits run through the shared save→wound→assign→apply child pipeline against the
    ///         target — the same machinery <see cref="StrafingStage"/>/ResolveImpactHitsStage and
    ///         RollToHitStage use. AP + the spell's pre-resolved weapon rules ride the synthetic weapon.</item>
    /// </list>
    ///
    /// DEFERRED (recorded in #033): the ±1 friendly-Caster assist (#094, slots in before the roll);
    /// multi-target damage (only the first target is hit — the synthetic pipeline runs once per cast, as
    /// Strafing's does); single-model targeting ("a unit of [1]").
    /// </summary>
    public class CastSpellStage : ParentStage<IUnitActionContext, ICombatMetadata>
    {
        public StageBinding OnFinished;

        private const string CANCEL_OPTION = "Cancel";
        private const int CAST_SUCCESS_THRESHOLD = 4;

        // Damage-spell pipeline parameters: set in Enter when a successful damage cast routes into the
        // save→wound children, read by GetNewChildContext when the child pipeline builds its metadata.
        // The hit-complete fold (Blast / on-6 rules) runs in Enter, so what's stored here is the FINAL
        // post-fold hit count plus the synthetic weapon and any carried save modifier (Rending).
        private DataBinding<UnitData> _damageTarget;
        private Weapon _damageWeapon = null!;
        private float _damageFinalHits;
        private int _damageSaveModifier;

        // #034 single-model targeting: when the cast spell targets one model ("a unit of [1]"), the chosen
        // model is picked in Enter and seeded as an IndividualTargetResult in GetNewChildContext, so the
        // child AssignWoundsStage confines all wounds to it (no carry-over). Null when whole-unit.
        private DataBinding<ModelData>? _damageIndividualModel;

        public CastSpellStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IUnitActionContext context)
        {
            _damageTarget = default;
            _damageFinalHits = 0f;
            _damageSaveModifier = 0;
            _damageIndividualModel = null;

            IUnit caster = context.ActivatingUnit.GetValue();
            PlayerID player = context.ActivatingPlayer();

            // Only castable spells are offered: affordable AND with at least one legal target. Filtering by
            // target here (and gating the Cast action the same way in ChooseActionStage.GetCanCast) is what
            // keeps a no-target cast from looping forever under a deterministic resolver — the same reason
            // ChooseRangedAttackStage filters weapons to those with a fireable target.
            int tokens = caster.Tokens.GetTokenCount(TokenType.SpellTokens);
            IReadOnlyList<RuntimeSpell> castable = GetCastableSpells(context.ActivatingUnit, player, tokens);
            if (castable.Count == 0)
            {
                GameContext.Log($"{caster.Name} has no castable spell (none affordable with a legal target).");
                await OnFinished.Activate(context);
                return;
            }

            // 1. Pick a spell (or cancel back to Choose Action).
            RuntimeSpell? chosen = await PickSpell(player, castable, tokens, caster.Name);
            if (chosen == null)
            {
                await OnFinished.Activate(context);
                return;
            }

            // 2. Build the eligible targets and let the player pick (up to the spell's MaxCount).
            List<DataBinding<UnitData>> candidates = SpellTargeting.GetEligibleTargets(
                GameContext, context.ActivatingUnit, player, chosen.Target);
            if (candidates.Count == 0)
            {
                GameContext.Log($"{chosen.Name} has no valid target in range or line of sight.");
                await OnFinished.Activate(context);
                return;
            }

            IReadOnlyList<DataBinding<UnitData>> targets = await PickTargets(player, chosen, candidates);
            if (targets.Count == 0)
            {
                // Cancelled before meeting the minimum target count — nothing spent.
                await OnFinished.Activate(context);
                return;
            }

            // 3. Spend the spell's token cost to attempt (spent whether or not the cast succeeds).
            caster.Tokens.RemoveTokens(TokenType.SpellTokens, chosen.Threshold);

            // 4. Cast roll: one die, 4+ succeeds. RollDecisive so it's a real outcome under the
            //    probabilistic roller. (±1 friendly-Caster assist — a #033 follow-up — would adjust here.)
            bool success = GameContext.DiceRoller.RollDecisive().AtOrAbove(CAST_SUCCESS_THRESHOLD) >= 1f;
            if (!success)
            {
                GameContext.Log($"{caster.Name} failed to cast {chosen.Name} (spent {chosen.Threshold} tokens).");
                await OnFinished.Activate(context);
                return;
            }

            GameContext.Log($"{caster.Name} cast {chosen.Name} (spent {chosen.Threshold} tokens).");

            // 5a. Damage spell → run the synthetic-hit save→wound child pipeline against the target.
            if (chosen.Effect is Effect.DealHits dealHits)
            {
                if (targets.Count > 1)
                {
                    GameContext.Log($"{chosen.Name} chose multiple targets, but spell damage is single-target " +
                        $"for now — only {targets[0].GetValue().Name} is hit (#034 multi-unit damage).");
                }
                _damageTarget = targets[0];

                // #034 single-model targeting: resolve "as a unit of [1]" — pick one model in the target unit
                // now (the cast has succeeded and is committed, so the pick is mandatory) and confine all
                // wounds to it via the IndividualTargetResult seeded in GetNewChildContext.
                if (chosen.Target.SingleModel)
                {
                    _damageIndividualModel = await PickIndividualModel(player, chosen.Name, _damageTarget);
                }

                // Synthetic spell weapon: the spell's AP + its pre-resolved weapon rules.
                _damageWeapon = new Weapon(chosen.Name, rangeInches: 0f, attacks: 0,
                    armorPenetration: dealHits.ArmorPenetration);
                foreach (ResolvedRule rule in chosen.WeaponRules)
                {
                    _damageWeapon.AttachRuleDefinition(rule);
                }

                // Run the hit-complete fold (Blast multiply, on-6 extra hits, Rending AP) over real rolled
                // dice before the save pipeline, so pre-save weapon rules actually fire on spell hits.
                (_damageFinalHits, _damageSaveModifier) =
                    ResolveSpellHits(caster, _damageTarget.GetValue(), dealHits.Count, _damageWeapon);

                await base.Enter(context); // child pipeline resolves the hits, then transitions to OnFinished
                return;
            }

            // 5b. Buff/debuff spell → apply the effect's token operations to each target.
            ApplyTokenEffect(caster, chosen, targets);
            await OnFinished.Activate(context);
        }

        // Applies a non-damage spell effect (e.g. AddRule "gets RULE once") to each target by running the
        // effect's polymorphic Apply against a per-target invocation, then committing the token operations.
        private void ApplyTokenEffect(IUnit caster, RuntimeSpell spell, IReadOnlyList<DataBinding<UnitData>> targets)
        {
            foreach (DataBinding<UnitData> target in targets)
            {
                RuleInvocation invocation = new RuleInvocation(
                    Hook: null, Bearer: caster, Arguments: System.Array.Empty<RuleArgument>(),
                    Target: target.GetValue(), DiceRoller: GameContext.DiceRoller);

                List<RuleOperation> operations = new List<RuleOperation>();
                spell.Effect.Apply(invocation, operations);
                OperationApplier.ApplyTokenOperations(operations);
            }
            GameContext.Log($"{spell.Name} affected {targets.Count} unit(s).");
        }

        protected override ICombatMetadata GetNewChildContext(IUnitActionContext contextSelf)
        {
            // The pre-folded synthetic weapon (AP + save/wound-phase rules like Bane/Deadly) and the
            // post-fold hit count are computed in Enter; here we just seed them for the save pipeline. The
            // hit faces no longer matter past this point (the hit-complete fold already ran), so the count
            // rides a cosmetic top-face seed; SaveModifier carries any AP promotion (Rending).
            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.ActivatingUnit,
                _damageTarget, _damageWeapon, weaponCount: 1, isMelee: false);

            RollToHitResults hitResults = new RollToHitResults(
                new List<SuccessfulHitInfo>() { new SuccessfulHitInfo(SyntheticHits(_damageFinalHits)) },
                new List<FailedHitInfo>());
            hitResults.SaveModifier = _damageSaveModifier;
            metadata.AddResult(hitResults);
            // No cover check runs for a synthetic spell hit; seed a zero bonus so the save stage won't throw.
            metadata.AddResult(new CoverCheckResults(0));

            // #034 single-model targeting: confine all wounds to the one chosen model (the same result
            // Takedown's BuildTargetListStage produces); AssignWoundsStage caps at its wounds, no carry-over.
            if (_damageIndividualModel != null)
            {
                metadata.AddResult(new IndividualTargetResult(_damageIndividualModel));
            }

            return metadata;
        }

        // #034 single-model targeting: pick one living model in the target unit ("a unit of [1]"). The cast
        // has already succeeded and its tokens are spent, so the pick is mandatory (no cancel) — mirroring
        // Takedown's BuildTargetListStage.MaybePickIndividualTarget.
        private async Task<DataBinding<ModelData>?> PickIndividualModel(PlayerID player, string spellName,
            DataBinding<UnitData> targetUnit)
        {
            List<DataBinding<ModelData>> living = targetUnit.ModelBindings()
                .Where(model => model.GetIsAlive())
                .ToList();

            // A legal damage target always has at least one living model; guard anyway so a degenerate
            // case falls back to whole-unit allocation rather than raising an option-less request.
            if (living.Count == 0)
            {
                return null;
            }

            List<SelectionRequest<ModelData>.ValidOption> validOptions = new();
            for (int i = 0; i < living.Count; i++)
            {
                validOptions.Add(new SelectionRequest<ModelData>.ValidOption(living[i], $"Model {i + 1}"));
            }

            SelectionRequest<ModelData> request = new SelectionRequest<ModelData>(player,
                $"{spellName}: choose the target model",
                validOptions, System.Array.Empty<SelectionRequest<ModelData>.InvalidOption>(), allowCancel: false);

            return await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<ModelData>, DataBinding<ModelData>>(request);
        }

        // Rolls the spell's hits as real dice and runs the hit-complete fold (the same machinery
        // RollToHitStage uses): Blast multiplies (capped at the target's living-model count), "on an
        // unmodified 6" rules add hits, and Rending promotes AP into a carried save modifier. The dice
        // faces don't gate the hits — every die is an automatic hit — they only feed the on-6 rules.
        // Returns the final hit count + the save modifier to seed into the save pipeline.
        private (float hits, int saveModifier) ResolveSpellHits(IUnit caster, IUnit target, int baseHits, Weapon spellWeapon)
        {
            IDiceResults rolled = GameContext.DiceRoller.Roll(baseHits);
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(caster, target, out _, out _,
                includeVertical: false);

            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.EvaluateAll(
                new HitRollCompleteContext(caster, target, rolled, distance, false, false),
                (caster, ERuleSeat.Actor, spellWeapon, (IReadOnlyList<IModel>?)null));

            HitInjectionSink injection = new HitInjectionSink();
            injection.ApplyFrom(ops);
            float hits = rolled.TotalRolls + injection.TotalExtraHits;

            HitMultiplierSink multiplier = new HitMultiplierSink();
            multiplier.ApplyFrom(ops);
            if (multiplier.NetMultiplier > 1)
            {
                hits = System.Math.Min(hits * multiplier.NetMultiplier, CountLivingModels(target));
            }

            RollModifierSink saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(ops);
            return (hits, saveModifiers.Net(ERollKind.Save));
        }

        private static int CountLivingModels(IUnit unit)
        {
            int count = 0;
            foreach (IModel model in unit.Models)
            {
                if (model.GetIsAlive()) count++;
            }
            return count;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnFinished = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnFinished), OnFinished, out string finishedEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(finishedEvent);

            return dictionary;
        }

        private IReadOnlyList<RuntimeSpell> GetCastableSpells(DataBinding<UnitData> caster, PlayerID player, int tokens)
        {
            ArmyData army = GameContext.GameDataStore().GetAllValues<ArmyData>()
                .FirstOrDefault(a => a.PlayerID == player);
            if (army == null)
            {
                return System.Array.Empty<RuntimeSpell>();
            }
            return army.Spells
                .Where(s => s.Threshold > 0 && s.Threshold <= tokens
                    && SpellTargeting.HasAnyEligibleTarget(GameContext, caster, player, s.Target))
                .ToList();
        }

        private async Task<RuntimeSpell?> PickSpell(PlayerID player, IReadOnlyList<RuntimeSpell> spells,
            int tokens, string casterName)
        {
            List<string> options = spells.Select(SpellOptionLabel).ToList();
            options.Add(CANCEL_OPTION);

            // Subtext under each spell: what it does. The Cancel option carries none.
            Dictionary<string, string> descriptions = new Dictionary<string, string>();
            foreach (RuntimeSpell spell in spells)
            {
                descriptions[SpellOptionLabel(spell)] = SpellText.Describe(spell.Definition);
            }

            string instructions =
                $"Choose a spell to cast - {casterName} has {tokens} spell token{(tokens == 1 ? "" : "s")}";

            StringSelectionRequest request = new StringSelectionRequest(player, instructions,
                options, System.Array.Empty<StringSelectionRequest.InvalidOption>(), descriptions);

            string choice = await GameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            if (choice == CANCEL_OPTION)
            {
                return null;
            }
            return spells.First(s => SpellOptionLabel(s) == choice);
        }

        private async Task<IReadOnlyList<DataBinding<UnitData>>> PickTargets(PlayerID player, RuntimeSpell spell,
            List<DataBinding<UnitData>> candidates)
        {
            List<DataBinding<UnitData>> chosen = new List<DataBinding<UnitData>>();
            List<DataBinding<UnitData>> remaining = new List<DataBinding<UnitData>>(candidates);

            for (int picked = 0; picked < spell.Target.MaxCount && remaining.Count > 0; picked++)
            {
                List<SelectionRequest<UnitData>.ValidOption> validOptions = remaining
                    .Select(u => new SelectionRequest<UnitData>.ValidOption(u, u.GetValue().Name))
                    .ToList();

                SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(player,
                    $"Choose target for {spell.Name} ({chosen.Count + 1} of up to {spell.Target.MaxCount})",
                    validOptions, System.Array.Empty<SelectionRequest<UnitData>.InvalidOption>(),
                    allowCancel: true);

                DataBinding<UnitData> target = await GameContext.PlayerRequester
                    .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);

                if (target == null)
                {
                    // Cancel stops target selection: proceed with what's chosen if the minimum is met,
                    // otherwise the caller treats it as cancelling the cast (nothing spent).
                    break;
                }

                chosen.Add(target);
                remaining.RemoveAll(u => u.Reference.Equals(target.Reference));
            }

            return chosen.Count >= spell.Target.MinCount ? chosen : new List<DataBinding<UnitData>>();
        }

        // Bridges a scalar hit count into the IDiceResults the save flow consumes (mirrors
        // StrafingStage.SyntheticHits / ResolveImpactHitsStage). The face is cosmetic — saves count by total.
        private static IDiceResults SyntheticHits(float count)
        {
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[perSide.Length - 1] = count;
            return new DiceResults(perSide);
        }

        private static string SpellOptionLabel(RuntimeSpell spell) => $"{spell.Name} ({spell.Threshold})";
    }
}
