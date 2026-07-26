using System.Collections.Generic;
using System.Linq;
using FDG.Data;
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
    /// Resolves a single "before attacking" activated ability the player picked in
    /// <see cref="ChooseActionStage"/> (an <see cref="AbilityOffer"/> stashed on the context via
    /// <c>SetPendingCustomAction</c>), then loops back to Choose Action via <see cref="OnFinished"/>.
    ///
    /// These abilities are authored at <see cref="EHookID.Activation_OnBeforeAttackAction"/> and surface as
    /// their own menu actions - like Cast, Teleport, or Storm - available whenever the unit has not yet
    /// attacked, whether or not it can attack anything. "Before attacking" in the rules means the window
    /// closes once the unit attacks (ChooseActionStage stops offering them after HasAttacked), NOT that they
    /// happen during an attack. Layered like <see cref="CustomActionStage"/> - it never sets
    /// HasMoved/HasAttacked, so it doesn't change what the unit may still do.
    ///
    /// Self abilities resolve against the bearer; Friend/Foe/Any abilities resolve their
    /// <c>TargetSelector</c> through <see cref="AbilityTargeting"/> and a
    /// <see cref="CancellableSelectionRequest{T}"/> so the player picks the unit(s).
    ///
    /// A <see cref="Effect.DealHits"/> ability (Breath Attack) resolves like <see cref="StrafingStage"/>:
    /// the hits ride a synthetic weapon carrying the effect's AP AND its <c>WithRules</c> weapon rules, and
    /// run the shared hit-complete fold (<see cref="SyntheticHitResolution"/>) before the save->wound child
    /// pipeline, so Blast/Rending/Surge apply exactly as they do to a fired volley (#164). Because each menu
    /// pick resolves exactly one ability and re-enters the menu, a second DealHits ability can still be used
    /// the same activation (a real limitation of the old post-attack prompt, which could not resume after
    /// the child pipeline).
    /// </summary>
    public class BeforeAttackActionStage : ParentStage<IUnitActionContext, ICombatMetadata>
    {
        public StageBinding OnFinished;

        // The DealHits target/hits/weapon computed in Enter and seeded into the child metadata. Only
        // meaningful between the accepted ability and the child pipeline running (StrafingStage pattern).
        private DataBinding<UnitData>? _pendingTarget;
        private int _pendingHits;
        private Weapon? _pendingWeapon;

        public BeforeAttackActionStage(IGameContext gameContext,
            IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
        }

        public override async Task Enter(IUnitActionContext context)
        {
            _pendingTarget = null;
            _pendingHits = 0;
            _pendingWeapon = null;

            AbilityOffer? offer = context.PendingCustomAction;

            // Defensive: the chooser always sets a pending offer before routing here. If somehow absent,
            // just return to Choose Action rather than throwing mid-activation.
            if (offer == null)
            {
                GameContext.Log("Before-attack action entered with no pending offer; returning to Choose Action.");
                await OnFinished.Activate(context);
                return;
            }

            IUnit unit = context.ActivatingUnit.GetValue();

            IReadOnlyList<DataBinding<UnitData>>? targetBindings =
                await SelectTargets(context, offer.Ability.TargetSelector);
            if (targetBindings == null)
            {
                // Backed out of (or couldn't complete) target selection - nothing applied, no cost paid.
                context.ClearPendingCustomAction();
                await OnFinished.Activate(context);
                return;
            }

            // Resolving the ability pays its cost and applies its effects - irreversible (#248: the pristine
            // back-out must no longer be offered once this activation has committed to an effect).
            context.MarkIrreversibleAction();

            IReadOnlyList<IUnit> targets = targetBindings.Select(b => (IUnit)b.GetValue()).ToList();
            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.ResolveAbility(offer, targets);
            OperationApplier.ApplyTokenOperations(ops);
            await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));
            GameContext.Log($"{unit.Name} used {offer.RuleName} before attacking.");

            context.ClearPendingCustomAction();

            // A DealHits ability resolves through the save->wound child pipeline (StrafingStage pattern):
            // seed the synthetic attack, run the children, and finish - the terminal event fires OnFinished
            // (back to the menu). A non-DealHits ability just loops back directly.
            RuleOperation.InvokeDealHits? dealHits =
                ops.OfType<RuleOperation.InvokeDealHits>().FirstOrDefault();
            if (dealHits != null && dealHits.Count > 0)
            {
                DataBinding<UnitData>? targetBinding = targetBindings
                    .FirstOrDefault(b => ReferenceEquals(b.GetValue(), dealHits.Target))
                    ?? targetBindings.FirstOrDefault();
                if (targetBinding != null)
                {
                    _pendingTarget = targetBinding;
                    _pendingHits = dealHits.Count;
                    _pendingWeapon = BuildAbilityWeapon(offer.RuleName, dealHits);
                    GameContext.Log($"{unit.Name}'s {offer.RuleName} deals {dealHits.Count} hit(s) at " +
                        $"AP({dealHits.ArmorPenetration}) to {targetBinding.GetValue().Name}.");

                    // Run the save->wound sub-pipeline; its terminal event fires OnFinished.
                    await base.Enter(context);
                    return;
                }
            }

            await OnFinished.Activate(context);
        }

        /// <summary>
        /// The synthetic weapon an ability's hits ride: the effect's AP, plus its <c>WithRules</c> resolved
        /// to weapon-scoped rules so Blast/Rending/Surge fold in (#164). Unlike a spell - which pre-resolves
        /// at army load - an ability may have been conferred at runtime by an aura or grant, so it has no
        /// load-time site and resolves here through the evaluator's shared resolver. A null resolver (bare
        /// harness / pre-rehydration resume) degrades to AP-only, matching the old behaviour rather than
        /// throwing.
        /// </summary>
        private Weapon BuildAbilityWeapon(string abilityName, RuleOperation.InvokeDealHits dealHits)
        {
            Weapon weapon = new Weapon(abilityName, rangeInches: 0f, attacks: 0,
                armorPenetration: dealHits.ArmorPenetration);

            IRuleResolver? resolver = GameContext.RuleEvaluator.RuleResolver;
            if (dealHits.WithRules.Count == 0 || resolver == null)
            {
                return weapon;
            }

            foreach (ResolvedRule rule in ArmyListSpellResolution.ResolveWeaponRuleNames(
                dealHits.WithRules, resolver, $"ability '{abilityName}'"))
            {
                weapon.AttachRuleDefinition(rule);
            }
            return weapon;
        }

        protected override ICombatMetadata GetNewChildContext(IUnitActionContext contextSelf)
        {
            // The DealHits come from an ability, not a weapon volley - model them as a synthetic attack so
            // the shared save/wound stages can consume them. The hits run through the same hit-complete fold
            // a fired volley does (#164), so the ability's own weapon rules apply.
            IUnit attacker = contextSelf.ActivatingUnit.GetValue();
            SyntheticHitResolution.Result hits = SyntheticHitResolution.Resolve(
                GameContext, attacker, _pendingTarget!.GetValue(), _pendingHits, _pendingWeapon!,
                isSpell: false);

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.ActivatingUnit,
                _pendingTarget!, _pendingWeapon!, weaponCount: 1, isMelee: false);

            RollToHitResults hitResults = new RollToHitResults(hits.HitGroups, new List<FailedHitInfo>());
            hitResults.SaveModifier = hits.SaveModifier;
            hitResults.ArmorPenetrationReduction = hits.ArmorPenetrationReduction;
            metadata.AddResult(hitResults);
            // No cover check runs for a synthetic ability hit; seed a zero bonus so the save stage won't throw.
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

        /// <summary>
        /// Resolves the ability's <see cref="TargetSelector"/> into the chosen target unit(s): the bearer
        /// for Self; otherwise the player picks between MinCount and MaxCount eligible units one at a time
        /// (each removed from the pool as it's taken). Returns null if the player backed out before meeting
        /// the minimum - the caller treats that as "ability not used." Returns bindings (not bare units) so
        /// a DealHits ability can seed the child pipeline's CombatMetadata with the picked target.
        /// </summary>
        private async Task<IReadOnlyList<DataBinding<UnitData>>?> SelectTargets(IUnitActionContext context,
            TargetSelector selector)
        {
            if (selector.TargetAffinity == ETargetAffinity.Self)
            {
                return new[] { context.ActivatingUnit };
            }

            List<DataBinding<UnitData>> remaining = AbilityTargeting.EligibleTargets(
                context.ActivatingUnit, selector, GameContext);
            List<DataBinding<UnitData>> chosen = new List<DataBinding<UnitData>>();

            while (chosen.Count < selector.MaxCount && remaining.Count > 0)
            {
                List<CancellableSelectionRequest<UnitData>.ValidOption> valid = remaining
                    .Select(b => new CancellableSelectionRequest<UnitData>.ValidOption(b, b.GetValue().Name))
                    .ToList();

                CancellableSelectionRequest<UnitData> request = new CancellableSelectionRequest<UnitData>(
                    context.ActivatingPlayer(),
                    $"Choose target ({chosen.Count + 1} of up to {selector.MaxCount})",
                    valid, new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

                CancellableResult<DataBinding<UnitData>> result = await GameContext.PlayerRequester
                    .RequestDecision<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(request);

                if (result is Cancelled<DataBinding<UnitData>>)
                {
                    // Cancelling past the minimum just stops adding extras; before it, it aborts the ability.
                    return chosen.Count >= selector.MinCount ? chosen : null;
                }

                DataBinding<UnitData> picked = ((Selected<DataBinding<UnitData>>)result).Value;
                chosen.Add(picked);
                remaining.Remove(picked);
            }

            return chosen.Count >= selector.MinCount ? chosen : null;
        }
    }
}
