using System.Collections.Generic;
using System.Linq;
using FDG.Data;
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
    /// #100 #2 — fires <see cref="EHookID.Activation_OnPreAttack"/> once the unit has committed to an
    /// attack action (Shoot or Charge), before targets/weapons resolve, and offers the pre-attack
    /// activated abilities a rule contributes there (buffs, Mend, Re-Position, marks). The acting player
    /// picks which to use (each gated by its own once-per-X <see cref="Cost"/>); the chosen ability is
    /// resolved and its token operations applied, then the stage hands off to the real attack via
    /// <see cref="OnFinished"/>. Layered like <see cref="CustomActionStage"/> — it never sets
    /// HasMoved/HasAttacked, so it doesn't change what the unit may still do.
    ///
    /// One instance sits on each attack edge of <see cref="ChooseActionStage"/> (Charge → melee,
    /// Shoot → shoot); the <see cref="EActionType"/> it carries is what the PreAttackContext reports.
    /// Charge is exact; the shoot edge passes <see cref="EActionType.Hold"/> as a best effort (there is no
    /// "Shoot" action type — shooting is a sub-step), which is fine because no corpus pre-attack ability
    /// gates on it. If one ever needs to distinguish shoot from melee here, give PreAttackContext a
    /// combat-kind rather than overloading the action type.
    ///
    /// Self abilities resolve against the bearer; Friend/Foe/Any abilities resolve their
    /// <c>TargetSelector</c> through <see cref="PreAttackTargeting"/> and a
    /// <see cref="CancellableSelectionRequest{T}"/> so the player picks the unit(s) (slice 2b).
    ///
    /// A <see cref="Effect.DealHits"/> ability (Breath Attack) resolves like <see cref="StrafingStage"/>:
    /// the hits ride a synthetic weapon carrying the effect's AP and run the shared save→wound child
    /// pipeline, then the stage finishes. StrafingStage's limitations are shared: at most ONE DealHits
    /// ability resolves per pre-attack entry (the menu does not resume after the child pipeline — the
    /// engine's await-chained transitions make looping past a child pipeline unsafe), and
    /// <c>DealHits.WithRules</c> is not applied (no rule resolver is reachable at stage runtime; warned
    /// loudly via <see cref="RuleDiagnostics"/> — see SpecialRulesAudit.md).
    /// </summary>
    public class PreAttackStage : ParentStage<IUnitActionContext, ICombatMetadata>
    {
        /// <summary> Sentinel option the player picks to stop using pre-attack abilities and attack. </summary>
        public const string DONE_CHOICE = "Done";

        public StageBinding OnFinished;
        private readonly EActionType _actionType;

        // The DealHits target/hits/weapon computed in Enter and seeded into the child metadata. Only
        // meaningful between the accepted ability and the child pipeline running (StrafingStage pattern).
        private DataBinding<UnitData>? _pendingTarget;
        private float _pendingHits;
        private Weapon? _pendingWeapon;

        public PreAttackStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent,
            EActionType actionType) : base(gameContext, parent)
        {
            _actionType = actionType;
        }

        // Distinct per instance (the melee-edge and shoot-edge copies differ by action type), so the two
        // siblings don't collide on the parent's transition key. See StageBase.Name.
        public override string Name => $"{nameof(PreAttackStage)}_{_actionType}";

        public override async Task Enter(IUnitActionContext context)
        {
            _pendingTarget = null;
            _pendingHits = 0f;
            _pendingWeapon = null;

            IUnit unit = context.ActivatingUnit.GetValue();

            // Used-this-entry guard: a pre-attack ability is offered at most once per attack regardless of
            // its cost, so a cost-free (mis-authored) ability can't loop the menu. Normal once-per-X
            // abilities are also dropped by their own cost gate after use; this is the belt to that braces.
            HashSet<string> usedThisEntry = new HashSet<string>();

            while (true)
            {
                // Offer only abilities that (a) haven't been used this attack and (b) actually have enough
                // valid targets to fire — so a "pick an enemy" ability with no enemy in range isn't shown.
                List<AbilityOffer> usable = GameContext.RuleEvaluator
                    .GatherOffers(new PreAttackContext(unit, _actionType))
                    .Where(o => !usedThisEntry.Contains(o.RuleName)
                                && PreAttackTargeting.EligibleTargets(context.ActivatingUnit,
                                       o.Ability.TargetSelector, GameContext).Count
                                   >= o.Ability.TargetSelector.MinCount)
                    .ToList();

                if (usable.Count == 0)
                {
                    break;
                }

                List<string> options = usable.Select(o => o.RuleName).ToList();
                options.Add(DONE_CHOICE);

                StringSelectionRequest request = new StringSelectionRequest(context.ActivatingPlayer(),
                    "Use a pre-attack ability?", options, new List<StringSelectionRequest.InvalidOption>());
                string choice = await GameContext.PlayerRequester
                    .RequestDecision<StringSelectionRequest, string>(request);

                if (choice == DONE_CHOICE)
                {
                    break;
                }

                AbilityOffer chosen = usable.First(o => o.RuleName == choice);
                // Picked → not re-offered this attack even if the player then backs out of target selection.
                usedThisEntry.Add(chosen.RuleName);

                IReadOnlyList<DataBinding<UnitData>>? targetBindings =
                    await SelectTargets(context, chosen.Ability.TargetSelector);
                if (targetBindings == null)
                {
                    // Backed out of (or couldn't complete) target selection — nothing applied, no cost paid.
                    continue;
                }

                IReadOnlyList<IUnit> targets = targetBindings.Select(b => (IUnit)b.GetValue()).ToList();
                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.ResolveAbility(chosen, targets);
                OperationApplier.ApplyTokenOperations(ops);
                GameContext.Log($"{unit.Name} used {chosen.RuleName} before attacking.");

                // A DealHits ability resolves through the save→wound child pipeline (StrafingStage
                // pattern): seed the synthetic attack, run the children ONCE, and finish. The menu loop
                // cannot resume after the child pipeline — its await only completes far downstream — so
                // this is deliberately a single-shot detour, mirroring StrafingStage.
                RuleOperation.InvokeDealHits? dealHits =
                    ops.OfType<RuleOperation.InvokeDealHits>().FirstOrDefault();
                if (dealHits != null && dealHits.Count > 0)
                {
                    DataBinding<UnitData>? targetBinding = targetBindings
                        .FirstOrDefault(b => ReferenceEquals(b.GetValue(), dealHits.Target))
                        ?? targetBindings.FirstOrDefault();
                    if (targetBinding != null)
                    {
                        if (dealHits.WithRules.Count > 0)
                        {
                            RuleDiagnostics.WarnOnce($"pre-attack-withrules:{chosen.RuleName}",
                                $"'{chosen.RuleName}' deals hits 'with' [{string.Join(", ", dealHits.WithRules)}], " +
                                "but weapon rules on a pre-attack DealHits ability are not applied yet - " +
                                "the hits resolve at the ability's AP only.");
                        }

                        _pendingTarget = targetBinding;
                        _pendingHits = dealHits.Count;
                        _pendingWeapon = new Weapon(chosen.RuleName, rangeInches: 0f, attacks: 0,
                            armorPenetration: dealHits.ArmorPenetration);
                        GameContext.Log($"{unit.Name}'s {chosen.RuleName} deals {dealHits.Count} hit(s) at " +
                            $"AP({dealHits.ArmorPenetration}) to {targetBinding.GetValue().Name}.");

                        // Run the save→wound sub-pipeline; its terminal event fires OnFinished.
                        await base.Enter(context);
                        return;
                    }
                }
            }

            await OnFinished.Activate(context);
        }

        protected override ICombatMetadata GetNewChildContext(IUnitActionContext contextSelf)
        {
            // The DealHits come from an ability, not a weapon volley — model them as a synthetic attack so
            // the shared save/wound stages can consume them (the histogram face is cosmetic; saves count
            // by TotalRolls).
            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.ActivatingUnit,
                _pendingTarget!, _pendingWeapon!, weaponCount: 1, isMelee: false);

            metadata.AddResult(new RollToHitResults(
                new List<SuccessfulHitInfo>() { new SuccessfulHitInfo(SyntheticHits(_pendingHits)) },
                new List<FailedHitInfo>()));
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

        // Bridges a scalar hit count into the IDiceResults the save flow consumes (mirrors
        // StrafingStage.SyntheticHits / ResolveImpactHitsStage). The face is cosmetic — saves count by
        // TotalRolls.
        private static IDiceResults SyntheticHits(float count)
        {
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[perSide.Length - 1] = count;
            return new DiceResults(perSide);
        }

        /// <summary>
        /// Resolves the ability's <see cref="TargetSelector"/> into the chosen target unit(s): the bearer
        /// for Self; otherwise the player picks between MinCount and MaxCount eligible units one at a time
        /// (each removed from the pool as it's taken). Returns null if the player backed out before meeting
        /// the minimum — the caller treats that as "ability not used." Returns bindings (not bare units) so
        /// a DealHits ability can seed the child pipeline's CombatMetadata with the picked target.
        /// </summary>
        private async Task<IReadOnlyList<DataBinding<UnitData>>?> SelectTargets(IUnitActionContext context,
            TargetSelector selector)
        {
            if (selector.TargetAffinity == ETargetAffinity.Self)
            {
                return new[] { context.ActivatingUnit };
            }

            List<DataBinding<UnitData>> remaining = PreAttackTargeting.EligibleTargets(
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
