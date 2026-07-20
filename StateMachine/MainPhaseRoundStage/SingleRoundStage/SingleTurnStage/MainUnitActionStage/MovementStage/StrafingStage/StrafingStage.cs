using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #042 Strafing: when a unit's move passes through an enemy unit's footprint, the mover may make a
    /// mid-move attack (3 hits) against that enemy, once per activation. Runs as a movement sub-stage AFTER
    /// the path is defined but BEFORE it is committed, so the path segments are read from the moving models'
    /// start positions. Mirrors ResolveImpactHitsStage: the hits have no weapon — they ride a synthetic AP-0
    /// attack seeded as a RollToHitResults, then run the existing save->wound sub-pipeline.
    ///
    /// The deal-hits effect is applied stage-side (not via the IOperationServices seam) because the
    /// save/wound pipeline is a child-stage chain; the engine's fire-and-forget transitions only sequence it
    /// correctly when it runs as a real child here, the way Impact runs inside the melee stage.
    ///
    /// The hits run through the shared hit-complete fold (<see cref="SyntheticHitResolution"/>) so the
    /// effect's own AP and <c>DealHits.WithRules</c> weapon rules apply (#164) — core Strafing carries
    /// neither, but a faction rule authored on this hook now gets them instead of having them dropped.
    ///
    /// DEFERRED (Appendix C): only the FIRST enemy crossed is offered (OncePerActivation caps to one strafe);
    /// and the OncePerActivation marker is granted but never auto-cleared (no activation-end token-clear
    /// yet), so a unit strafes at most once per game.
    /// </summary>
    public class StrafingStage : ParentStage<IMovementActionContext, ICombatMetadata>
    {
        public StageBinding OnStrafeResolved;

        // The enemy unit the accepted strafe targets and the hit count, computed in Enter and seeded into
        // the child metadata. Only meaningful between Enter and the child pipeline running.
        private DataBinding<UnitData> _targetEnemy;
        private int _hitCount;
        // The synthetic weapon the strafe hits ride: the effect's AP plus its resolved WithRules (#164).
        private Weapon? _strafeWeapon;

        public StrafingStage(IGameContext gameContext, IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IMovementActionContext context)
        {
            _targetEnemy = default;
            _hitCount = 0;
            _strafeWeapon = null;

            if (!context.TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths) || paths.Count == 0)
            {
                await OnStrafeResolved.Activate(context);
                return;
            }

            List<DataBinding<UnitData>> crossed = MovementUtilities.GetEnemyUnitsMovedThrough(
                paths, context.MovingUnit, GameContext);
            if (crossed.Count == 0)
            {
                await OnStrafeResolved.Activate(context);
                return;
            }

            IUnit mover = context.MovingUnit.GetValue();
            IReadOnlyList<AbilityOffer> offers = GameContext.RuleEvaluator
                .GatherOffers(new MoveThroughEnemyContext(mover));
            if (offers.Count == 0)
            {
                await OnStrafeResolved.Activate(context);
                return;
            }

            // The path may cross several enemies; OncePerActivation allows one strafe, so target the first.
            DataBinding<UnitData> enemy = crossed[0];

            foreach (AbilityOffer offer in offers)
            {
                var question = new YesNoRequest(mover.PlayerID,
                    $"Use {offer.RuleName}: {mover.Name} attacks {enemy.GetValue().Name} while moving through?",
                    defaultAnswer: true);
                bool accepted = await GameContext.PlayerRequester
                    .RequestDecision<YesNoRequest, bool>(question);

                if (!accepted) continue;

                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                    .ResolveAbility(offer, new[] { (IUnit)enemy.GetValue() });

                // Apply the cost marker (the once-per-activation gate) via the shared token applier;
                // OperationExecutor runs only ExecutableOperations, so it never applies cost tokens -
                // both are needed to cover the full operation queue.
                OperationApplier.ApplyTokenOperations(ops);
                await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));

                RuleOperation.InvokeDealHits? dealHits =
                    ops.OfType<RuleOperation.InvokeDealHits>().FirstOrDefault();
                if (dealHits != null && dealHits.Count > 0)
                {
                    _targetEnemy = enemy;
                    _hitCount = dealHits.Count;
                    _strafeWeapon = BuildStrafeWeapon(offer.RuleName, dealHits);
                    GameContext.Log($"{mover.Name} used {offer.RuleName}, dealing {dealHits.Count} hits to " +
                        $"{enemy.GetValue().Name} while moving through.");

                    // Run the save->wound sub-pipeline against the strafe hits.
                    await base.Enter(context);
                    return;
                }
            }

            await OnStrafeResolved.Activate(context);
        }

        /// <summary>
        /// The synthetic weapon the strafe hits ride: the effect's own AP (previously hardcoded to 0, which
        /// silently dropped an authored AP) plus its <c>WithRules</c> resolved to weapon-scoped rules (#164).
        /// A null resolver (bare harness / pre-rehydration resume) degrades to AP-only rather than throwing.
        /// </summary>
        private Weapon BuildStrafeWeapon(string ruleName, RuleOperation.InvokeDealHits dealHits)
        {
            Weapon weapon = new Weapon(ruleName, rangeInches: 0f, attacks: 0,
                armorPenetration: dealHits.ArmorPenetration);

            IRuleResolver? resolver = GameContext.RuleEvaluator.RuleResolver;
            if (dealHits.WithRules.Count == 0 || resolver == null)
            {
                return weapon;
            }

            foreach (ResolvedRule rule in ArmyListSpellResolution.ResolveWeaponRuleNames(
                dealHits.WithRules, resolver, $"strafe rule '{ruleName}'"))
            {
                weapon.AttachRuleDefinition(rule);
            }
            return weapon;
        }

        protected override ICombatMetadata GetNewChildContext(IMovementActionContext contextSelf)
        {
            // Strafe hits come from passing fire — model them as a synthetic attack so the shared save/wound
            // stages can consume them. The hits run the same hit-complete fold a fired volley does (#164).
            IUnit mover = contextSelf.MovingUnit.GetValue();
            SyntheticHitResolution.Result hits = SyntheticHitResolution.Resolve(
                GameContext, mover, _targetEnemy.GetValue(), _hitCount, _strafeWeapon!, isSpell: false);

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.MovingUnit,
                _targetEnemy, _strafeWeapon!, weaponCount: 1, isMelee: false);

            RollToHitResults hitResults = new RollToHitResults(hits.HitGroups, new List<FailedHitInfo>());
            hitResults.SaveModifier = hits.SaveModifier;
            hitResults.ArmorPenetrationReduction = hits.ArmorPenetrationReduction;
            metadata.AddResult(hitResults);
            // No cover check runs for a synthetic strafe; seed a zero bonus so the shared save stage won't throw.
            metadata.AddResult(new CoverCheckResults(0));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnStrafeResolved = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnStrafeResolved), OnStrafeResolved, out string strafeResolvedEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(strafeResolvedEvent);

            return dictionary;
        }

    }
}
