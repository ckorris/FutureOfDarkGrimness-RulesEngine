using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P10 Crossing Attack: when a unit moves through an enemy unit, once per activation it may pick
    /// that enemy and roll X dice - each 6+ deals one DIRECT wound (no armor save, but Regeneration/Tough
    /// still apply). The auto-wound sibling of <see cref="StrafingStage"/>: same move-through-enemy trigger,
    /// same YesNo offer, but the ability's <see cref="Effect.DealAutoWounds"/> queues an
    /// <see cref="RuleOperation.InvokeDealAutoWounds"/> that this stage rolls through
    /// <see cref="SyntheticWoundResolution"/> and feeds straight into wound assignment, SKIPPING the save
    /// stages (the same short pipeline as <c>ResolveRavageWoundsStage</c>).
    ///
    /// Runs right after <see cref="StrafingStage"/> in the movement flow, before the move commits, so
    /// move-through detection reads each model's start position. It offers ONLY DealAutoWounds abilities and
    /// StrafingStage offers ONLY DealHits ones, so the two never double-offer or double-charge a rule that
    /// happens to sit at the same hook.
    ///
    /// DEFERRED (mirrors StrafingStage): only the FIRST enemy crossed is offered (OncePerActivation caps to
    /// one), and the once-per-activation marker is not auto-cleared, so a unit crosses at most once per game.
    /// </summary>
    public class CrossingAttackStage : ParentStage<IMovementActionContext, ICombatMetadata>
    {
        public StageBinding OnCrossingResolved;

        // The enemy the accepted crossing targets and the wound count rolled, computed in Enter and seeded
        // into the child metadata. Only meaningful between Enter and the child pipeline running.
        private DataBinding<UnitData> _targetEnemy;
        private float _woundCount;

        public CrossingAttackStage(IGameContext gameContext, IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IMovementActionContext context)
        {
            _targetEnemy = default;
            _woundCount = 0f;

            if (!context.TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths) || paths.Count == 0)
            {
                await OnCrossingResolved.Activate(context);
                return;
            }

            List<DataBinding<UnitData>> crossed = MovementUtilities.GetEnemyUnitsMovedThrough(
                paths, context.MovingUnit, GameContext);
            if (crossed.Count == 0)
            {
                await OnCrossingResolved.Activate(context);
                return;
            }

            IUnit mover = context.MovingUnit.GetValue();
            // Only auto-wound (Crossing Attack) abilities are ours; DealHits abilities belong to StrafingStage.
            IReadOnlyList<AbilityOffer> offers = GameContext.RuleEvaluator
                .GatherOffers(new MoveThroughEnemyContext(mover))
                .Where(o => o.Ability.Effect is Effect.DealAutoWounds)
                .ToList();
            if (offers.Count == 0)
            {
                await OnCrossingResolved.Activate(context);
                return;
            }

            // The path may cross several enemies; OncePerActivation allows one crossing, so target the first.
            DataBinding<UnitData> enemy = crossed[0];

            foreach (AbilityOffer offer in offers)
            {
                var question = new YesNoRequest(mover.PlayerID,
                    $"Use {offer.RuleName}: {mover.Name} strikes {enemy.GetValue().Name} while moving through?",
                    defaultAnswer: true);
                bool accepted = await GameContext.PlayerRequester
                    .RequestDecision<YesNoRequest, bool>(question);

                if (!accepted) continue;

                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                    .ResolveAbility(offer, new[] { (IUnit)enemy.GetValue() });

                OperationApplier.ApplyTokenOperations(ops);
                await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));

                RuleOperation.InvokeDealAutoWounds? autoWounds =
                    ops.OfType<RuleOperation.InvokeDealAutoWounds>().FirstOrDefault();
                if (autoWounds != null && autoWounds.DiceCount > 0)
                {
                    IDiceResults wounds = await SyntheticWoundResolution.RollWoundPool(GameContext,
                        autoWounds.DiceCount, autoWounds.SuccessThreshold, offer.RuleName, enemy.GetValue().Name);

                    GameContext.Log($"{mover.Name} used {offer.RuleName}, dealing {wounds.TotalRolls:0.##} " +
                        $"unsaveable wound(s) to {enemy.GetValue().Name} while moving through.");

                    if (wounds.TotalRolls > 0f)
                    {
                        _targetEnemy = enemy;
                        _woundCount = wounds.TotalRolls;

                        // Run the save-skipping assign -> apply sub-pipeline.
                        await base.Enter(context);
                        return;
                    }

                    await OnCrossingResolved.Activate(context);
                    return;
                }
            }

            await OnCrossingResolved.Activate(context);
        }

        protected override ICombatMetadata GetNewChildContext(IMovementActionContext contextSelf)
        {
            // A weaponless auto-wound: a synthetic AP-0 attack carrying PRE-FAILED saves, so no save is
            // rolled and only the defender's Regeneration/Tough apply (the bare weapon carries no rules).
            Weapon crossingWeapon = new Weapon("Crossing Attack", rangeInches: 0f, attacks: 0, armorPenetration: 0);

            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.MovingUnit,
                _targetEnemy, crossingWeapon, weaponCount: 1, isMelee: false);

            metadata.AddResult(SyntheticWoundResolution.AsUnsavedWounds(
                SyntheticHitResolution.SyntheticHits(_woundCount)));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnCrossingResolved = new StageBinding(this);

            // No DetermineSaveRollsNeeded / RollToSave: the wounds are unsaveable, so the pipeline starts at
            // wound assignment (Regeneration/Tough) and applies.
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnCrossingResolved), OnCrossingResolved, out string crossingResolvedEvent)
                .Build();

            startingChild = assignWounds;

            assignWounds.BindNextStage(applyWounds)
                .BindToEvent(crossingResolvedEvent);

            return dictionary;
        }
    }
}
