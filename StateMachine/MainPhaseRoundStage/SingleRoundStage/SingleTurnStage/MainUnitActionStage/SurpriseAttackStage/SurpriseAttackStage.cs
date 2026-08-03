using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 Surprise Attack: "Counts as having Infiltrate. The first time this unit is activated, pick one
    /// enemy unit within 6in in line of sight, and roll X dice. For each 2+ it takes one hit with AP(1)."
    /// This stage owns the second clause (the first is a <see cref="Effect.DeferDeployment"/> passive, the
    /// same one Infiltrate carries); it runs immediately after <see cref="ActivationStartStage"/> and before
    /// the action menu, so the 6in is measured from where the unit started its activation.
    ///
    /// <para>The burst is MANDATORY, not a menu action (owner ruling 2026-07-30): the text says "the first
    /// time this unit is activated", so it fires by itself rather than being offered like Storm - which
    /// would let the player defer it to a later activation. The only decision is WHICH enemy, and only when
    /// more than one is eligible; the pick is therefore a non-cancellable
    /// <see cref="SelectionRequest{T}"/>, and a single eligible enemy resolves with no prompt at all
    /// (<see cref="ActivationStartStage"/>'s "nothing to choose" precedent).</para>
    ///
    /// <para>"The FIRST time" is enforced by paying the ability's <see cref="Cost.OncePerGame"/> on this
    /// activation whether or not anything was in range (owner ruling 2026-07-30): a unit that activates with
    /// every enemy over 6in away, or with none in sight, loses the burst rather than banking it. The cost is
    /// therefore paid BEFORE the target search, which is also what makes this stage safe to re-enter.</para>
    ///
    /// <para>The pool is rolled FRACTIONALLY, unlike <c>StormStage</c>'s: there each success picks a target,
    /// so the count had to be decisive, but here the successes ARE the hit count - a quantity that stays
    /// fractional under the probabilistic roller (the #100 dice invariant). The success sub-histogram is
    /// handed straight to <see cref="SyntheticHitResolution.ResolveRolled"/> so the hits fold exactly like a
    /// fired volley's before running the shared save -> wound children.</para>
    /// </summary>
    public class SurpriseAttackStage : ParentStage<IUnitActionContext, ICombatMetadata>
    {
        /// <summary>
        /// The target pick's instruction prefix, with the rule's name appended. Leading (rather than
        /// trailing) so it is a stable discriminator: <c>TacticianUnitSelectionResolver</c> keys the AI's
        /// value-weighted pick on it, exactly as it keys spell targets and deploy order on theirs.
        /// </summary>
        public const string PICK_INSTRUCTION_PREFIX = "Pick the enemy unit hit by ";

        /// <summary> The burst resolved (or there was none); continue to the action menu. </summary>
        public StageBinding OnFinished;

        // The picked target, the rolled hits and the synthetic weapon they ride. Only meaningful between
        // Enter's roll and the child pipeline reading them (the BeforeAttackActionStage pattern).
        private DataBinding<UnitData>? _target;
        private IDiceResults? _hits;
        private Weapon? _weapon;

        public SurpriseAttackStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IUnitActionContext context)
        {
            _target = null;
            _hits = null;
            _weapon = null;

            IUnit unit = context.ActivatingUnit.GetValue();

            // One offer per activation is all the corpus needs (both carriers are single-model units with a
            // single Surprise Attack). A second pooled-hit rule on one unit would need the StormStage queue
            // loop, since the save/wound children can only run once per entry.
            AbilityOffer? offer = GameContext.RuleEvaluator.GatherOffers(new ActivationStartContext(unit))
                .FirstOrDefault(o => o.Ability.Effect is Effect.DealPooledHits);

            if (offer == null)
            {
                await OnFinished.Activate(context);
                return;
            }

            // Spending the once-per-game marker and rolling dice cannot be backed out of (#248).
            context.MarkIrreversibleAction();

            DataBinding<UnitData>? target = await PickTarget(context, offer);

            // ResolveAbility emits the cost token ops AND the InvokeDealPooledHits op; only the token ops are
            // applied here (the pool is rolled below off the op's config), mirroring StormStage. With no
            // eligible enemy the ability still resolves - against the bearer, whose op we drop - because the
            // FIRST activation is the only one that gets the burst, in range or not.
            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.ResolveAbility(offer,
                new[] { target != null ? (IUnit)target.GetValue() : unit });
            OperationApplier.ApplyTokenOperations(ops);

            RuleOperation.InvokeDealPooledHits? pooled =
                ops.OfType<RuleOperation.InvokeDealPooledHits>().FirstOrDefault();

            if (target == null || pooled == null)
            {
                GameContext.Log($"{unit.Name}'s {offer.RuleName}: no enemy within " +
                    $"{offer.Ability.TargetSelector.RangeInches:0.##}in in line of sight - the surprise is spent.");
                await OnFinished.Activate(context);
                return;
            }

            // Fractional by construction: the successes are the HIT COUNT, so the sub-histogram's TotalRolls
            // is kept as-is rather than int-locked (the #100 dice invariant; contrast StormStage's decisive
            // pool, whose successes are a number of target PICKS).
            IDiceResults pool = GameContext.DiceRoller.Roll(pooled.DiceCount);
            IDiceResults hits = pool.SubsetAtOrAbove(pooled.SuccessThreshold);

            await GameContext.Presenter.Present(DiceRolledBeat.From(pool, pooled.SuccessThreshold,
                GameContext.Settings.RandomnessType, offer.RuleName, RollTags.Count(hits.TotalRolls, "hit"),
                category: ERollBeatCategory.Offense, context: unit.Name));
            GameContext.Log($"{unit.Name}'s {offer.RuleName}: rolled {pooled.DiceCount} dice at " +
                $"{target.GetValue().Name} -> {hits.TotalRolls:0.##} hit(s) at AP({pooled.ArmorPenetration}).");

            if (hits.TotalRolls <= 0f)
            {
                await OnFinished.Activate(context);
                return;
            }

            _target = target;
            _hits = hits;
            _weapon = new Weapon(offer.RuleName, rangeInches: 0f, attacks: 0,
                armorPenetration: pooled.ArmorPenetration);

            // Run the save -> wound sub-pipeline; its terminal event fires OnFinished.
            await base.Enter(context);
        }

        /// <summary>
        /// The one enemy that takes the burst, or null when nothing is eligible. A single candidate is taken
        /// without a prompt - the rule leaves no choice to make - and several are chosen through a
        /// non-cancellable request, since "pick one enemy unit" is mandatory and there is nowhere to back
        /// out to this early in the activation.
        /// </summary>
        private async Task<DataBinding<UnitData>?> PickTarget(IUnitActionContext context, AbilityOffer offer)
        {
            List<DataBinding<UnitData>> eligible = AbilityTargeting.EligibleTargets(
                context.ActivatingUnit, offer.Ability.TargetSelector, GameContext);

            if (eligible.Count == 0)
            {
                return null;
            }

            if (eligible.Count == 1)
            {
                return eligible[0];
            }

            List<SelectionRequest<UnitData>.ValidOption> valid = eligible
                .Select(b => new SelectionRequest<UnitData>.ValidOption(b, b.GetValue().Name))
                .ToList();

            SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(
                context.ActivatingPlayer(),
                PICK_INSTRUCTION_PREFIX + offer.RuleName,
                valid, new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: false,
                displayName: $"Choosing a {offer.RuleName} Target");

            return await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);
        }

        protected override ICombatMetadata GetNewChildContext(IUnitActionContext contextSelf)
        {
            // The pool successes are the hits: fold them through the shared hit-complete pass (so the
            // defender's AP reduction and any on-6 rules apply) and hand the groups to the save stages.
            IUnit attacker = contextSelf.ActivatingUnit.GetValue();
            SyntheticHitResolution.Result hits = SyntheticHitResolution.ResolveRolled(
                GameContext, attacker, _target!.GetValue(), _hits!, _weapon!, isSpell: false);

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.ActivatingUnit,
                _target!, _weapon!, weaponCount: 1, isMelee: false);

            RollToHitResults hitResults = new RollToHitResults(hits.HitGroups, new List<FailedHitInfo>());
            hitResults.SaveModifier = hits.SaveModifier;
            hitResults.ArmorPenetrationReduction = hits.ArmorPenetrationReduction;
            metadata.AddResult(hitResults);
            // No cover check for a synthetic burst; seed a zero bonus so the shared save stage won't throw.
            metadata.AddResult(new CoverCheckResults(0));

            return metadata;
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
    }
}
