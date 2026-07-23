using System.Collections.Generic;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P10 Storm of X: "once per game, when activated, before attacking, roll N dice; for each 2+ pick
    /// one enemy unit within R and deal K hits with [rule]." Offered in Choose Action (like Teleport) and
    /// routed here; fully layered, so after it resolves the unit still moves/attacks normally.
    ///
    /// The pool is rolled DECISIVELY so the number of target picks is a whole number even under the
    /// probabilistic roller - you cannot pick a fractional number of targets (the #100 dice invariant, the
    /// same call as P15's branch die). Each success independently picks a target (owner ruling 2026-07-22),
    /// and each target's K hits run the real save/wound pipeline through <see cref="SyntheticHitResolution"/>
    /// (so Shred/Surge/Bane and AP apply, #164). Because that pipeline can only run as a real child stage
    /// once per entry, the per-target batches are a LOOP: <see cref="OnBatchDone"/> re-enters this stage,
    /// which dequeues the next target; when the queue drains, <see cref="OnAllDone"/> returns to the menu.
    /// </summary>
    public class StormStage : ParentStage<IUnitActionContext, ICombatMetadata>
    {
        /// <summary> One target's hit batch resolved; bound back to this stage to process the next. </summary>
        public StageBinding OnBatchDone;
        /// <summary> The queue is drained; return to the action menu (the unit may still attack). </summary>
        public StageBinding OnAllDone;

        // Built once on the first entry (the setup pass), then drained one target per re-entry. Null between
        // uses so a later activation's Storm rebuilds cleanly. Fields (not context state) because only this
        // stage reads them and only one unit activates at a time.
        private Queue<DataBinding<UnitData>>? _queue;
        private Weapon? _weapon;
        private int _hitsPerTarget;
        private DataBinding<UnitData> _currentTarget;

        public StormStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IUnitActionContext context)
        {
            // First entry: roll the pool, pay the cost, and collect the per-success target picks.
            if (_queue == null)
            {
                await SetUpStorm(context);
            }

            // Queue drained (or nothing to do): reset and hand control back to the menu.
            if (_queue!.Count == 0)
            {
                _queue = null;
                _weapon = null;
                _hitsPerTarget = 0;
                await OnAllDone.Activate(context);
                return;
            }

            // Resolve the next target's hit batch through the save/wound child pipeline.
            _currentTarget = _queue.Dequeue();
            await base.Enter(context);
        }

        private async Task SetUpStorm(IUnitActionContext context)
        {
            IUnit unit = context.ActivatingUnit.GetValue();
            AbilityOffer offer = context.PendingCustomAction;
            context.ClearPendingCustomAction();

            Effect.StormOfHits config = (Effect.StormOfHits)offer.Ability.Effect;

            // Pay the once-per-game cost now - choosing Storm from the menu and rolling the dice is the
            // commitment (there is no back-out). ResolveAbility emits the cost token ops (and the InvokeStorm
            // op, which we ignore - the config is read straight off the effect); apply only the token ops.
            IReadOnlyList<RuleOperation> costOps = GameContext.RuleEvaluator.ResolveAbility(offer, new[] { unit });
            OperationApplier.ApplyTokenOperations(costOps);

            // Roll the pool DECISIVELY: each die commits to a concrete face even in probabilistic mode, so the
            // success count - the number of target picks - is a whole number. A fractional pick is meaningless.
            int successes = 0;
            float[] faces = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            for (int i = 0; i < config.PoolDice; i++)
            {
                int face = GameContext.DiceRoller.RollDecisiveFace();
                faces[face - 1] += 1f;
                if (face >= config.SuccessThreshold) successes++;
            }

            await GameContext.Presenter.Present(DiceRolledBeat.From(new DiceResults(faces), config.SuccessThreshold,
                GameContext.Settings.RandomnessType, offer.RuleName, $"{successes} success(es)",
                category: ERollBeatCategory.Offense, context: unit.Name));
            GameContext.Log($"{unit.Name}'s {offer.RuleName}: rolled {config.PoolDice} dice -> {successes} success(es).");

            _weapon = BuildStormWeapon(offer.RuleName, config);
            _hitsPerTarget = config.HitsPerSuccess;

            // One target pick per success; a success with no enemy in range (or a cancelled pick) is lost.
            TargetSelector selector = new TargetSelector(config.RangeInches, 1, 1, ETargetAffinity.Foe, false);
            _queue = new Queue<DataBinding<UnitData>>();
            for (int s = 0; s < successes; s++)
            {
                DataBinding<UnitData>? pick = await PickTarget(context, selector, s + 1, successes);
                if (pick != null) _queue.Enqueue(pick);
            }
        }

        /// <summary>
        /// Prompts the acting player for one enemy within range. Returns null when nothing is eligible or the
        /// player cancels - that success simply deals no hits.
        /// </summary>
        private async Task<DataBinding<UnitData>?> PickTarget(IUnitActionContext context, TargetSelector selector,
            int ordinal, int total)
        {
            List<DataBinding<UnitData>> eligible = PreAttackTargeting.EligibleTargets(
                context.ActivatingUnit, selector, GameContext);
            if (eligible.Count == 0)
            {
                GameContext.Log($"Storm success {ordinal}/{total}: no enemy in range, hits lost.");
                return null;
            }

            List<CancellableSelectionRequest<UnitData>.ValidOption> valid = eligible
                .Select(b => new CancellableSelectionRequest<UnitData>.ValidOption(b, b.GetValue().Name))
                .ToList();

            CancellableSelectionRequest<UnitData> request = new CancellableSelectionRequest<UnitData>(
                context.ActivatingPlayer(),
                $"Storm target ({ordinal} of {total}) - pick an enemy to take the hits",
                valid, new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

            CancellableResult<DataBinding<UnitData>> result = await GameContext.PlayerRequester
                .RequestDecision<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(request);

            return result is Selected<DataBinding<UnitData>> selected ? selected.Value : null;
        }

        /// <summary>
        /// The synthetic weapon the storm hits ride: its AP plus the storm's rule (Shred / Surge / Bane)
        /// resolved to weapon scope, so the #164 fold applies them exactly as for a spell's DealHits.
        /// </summary>
        private Weapon BuildStormWeapon(string ruleName, Effect.StormOfHits config)
        {
            Weapon weapon = new Weapon(ruleName, rangeInches: 0f, attacks: 0, armorPenetration: config.ArmorPenetration);

            IRuleResolver? resolver = GameContext.RuleEvaluator.RuleResolver;
            if (config.WithRules.Count == 0 || resolver == null)
            {
                return weapon;
            }

            foreach (ResolvedRule rule in ArmyListSpellResolution.ResolveWeaponRuleNames(
                config.WithRules, resolver, $"storm '{ruleName}'"))
            {
                weapon.AttachRuleDefinition(rule);
            }
            return weapon;
        }

        protected override ICombatMetadata GetNewChildContext(IUnitActionContext contextSelf)
        {
            // This target's K hits, run through the shared hit-complete fold so the storm's rule and AP apply.
            IUnit attacker = contextSelf.ActivatingUnit.GetValue();
            SyntheticHitResolution.Result hits = SyntheticHitResolution.Resolve(
                GameContext, attacker, _currentTarget.GetValue(), _hitsPerTarget, _weapon!, isSpell: false);

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.ActivatingUnit,
                _currentTarget, _weapon!, weaponCount: 1, isMelee: false);

            RollToHitResults hitResults = new RollToHitResults(hits.HitGroups, new List<FailedHitInfo>());
            hitResults.SaveModifier = hits.SaveModifier;
            hitResults.ArmorPenetrationReduction = hits.ArmorPenetrationReduction;
            metadata.AddResult(hitResults);
            // No cover check for a synthetic storm hit; seed a zero bonus so the shared save stage won't throw.
            metadata.AddResult(new CoverCheckResults(0));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnBatchDone = new StageBinding(this);
            OnAllDone = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnBatchDone), OnBatchDone, out string batchDoneEvent)
                .AddSibling(nameof(OnAllDone), OnAllDone, out string allDoneEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(batchDoneEvent);

            return dictionary;
        }
    }
}
