using System.Collections.Generic;
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
    /// Strafing (#042, reworked #197): "Once per activation, when this model moves through enemy units, pick
    /// one of them and attack it with this weapon as if it was shooting." Runs as a movement sub-stage AFTER
    /// the path is defined but BEFORE it is committed, so the path segments are read from the moving models'
    /// start positions.
    ///
    /// <para>The child chain IS the shooting pipeline, minus range and occlusion: the mover is passing
    /// directly over the target, and every corpus strafe weapon has range 0 precisely because it can be used
    /// no other way — a range check would reject the one attack the weapon exists to make. Everything else is
    /// the real thing, so the weapon's own profile applies: its Attacks, its AP, and its rules (Blast,
    /// Deadly, Shred, Rending) folding through the shared hit and save stages at the bearer's Quality. That
    /// is the point of the #197 rework — the rule used to deal a flat 3 synthetic hits, which is not what any
    /// of its 12 corpus references say.</para>
    ///
    /// <para>The attack operation is enacted stage-side rather than through the <c>IOperationServices</c>
    /// seam because that chain is a child-stage pipeline: the engine's fire-and-forget stage transitions
    /// only sequence correctly when it runs as a real child here, the way Impact's hits run inside the melee
    /// stage — not when driven from a service call that would return before the AssignWounds request is
    /// answered.</para>
    ///
    /// <para>Sits beside <see cref="CrossingAttackStage"/>, which handles the auto-wound sibling at the same
    /// hook; each filters the offers to its own effect type so neither pays the other's once-per-activation
    /// cost and then does nothing.</para>
    /// </summary>
    public class StrafingStage : ParentStage<IMovementActionContext, ICombatMetadata>
    {
        public StageBinding OnStrafeResolved;

        // The enemy unit the accepted strafe targets, the weapon it is made with, and that enemy's wounds
        // before the attack (the morale baseline). Only meaningful between Enter and the child chain running.
        private DataBinding<UnitData> _targetEnemy;
        private IWeapon? _weapon;
        private float _defenderWoundsBefore;

        public StrafingStage(IGameContext gameContext, IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IMovementActionContext context)
        {
            _targetEnemy = default;
            _weapon = null;
            _defenderWoundsBefore = 0f;

            if (!context.TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths) || paths.Count == 0)
            {
                await OnStrafeResolved.Activate(context);
                return;
            }

            IUnit mover = context.MovingUnit.GetValue();
            WarnIfStrafeWeaponCannotReachAnEnemy(mover);

            List<DataBinding<UnitData>> crossed = MovementUtilities.GetEnemyUnitsMovedThrough(
                paths, context.MovingUnit, GameContext);
            if (crossed.Count == 0)
            {
                await OnStrafeResolved.Activate(context);
                return;
            }

            // Only attack-with-this-weapon (Strafing) abilities are ours; #197 P10 Crossing Attack's
            // DealAutoWounds abilities sit at the same hook and are handled by CrossingAttackStage, so skip
            // them here - otherwise this stage would prompt and pay their once-per-activation cost, then deal
            // no hits.
            IReadOnlyList<AbilityOffer> offers = GameContext.RuleEvaluator
                .GatherOffers(new MoveThroughEnemyContext(mover))
                .Where(o => o.Ability.Effect is Effect.AttackWithThisWeapon)
                .ToList();
            if (offers.Count == 0)
            {
                await OnStrafeResolved.Activate(context);
                return;
            }

            foreach (AbilityOffer offer in offers)
            {
                DataBinding<UnitData>? enemy = await PickTargetCrossed(mover, offer, crossed);
                if (enemy == null) continue;

                // Resolved only AFTER the pick, so declining costs nothing: ResolveAbility emits the
                // once-per-activation marker along with the effect.
                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                    .ResolveAbility(offer, new[] { (IUnit)enemy.GetValue() });

                // The cost marker rides the shared token applier; OperationExecutor runs only
                // ExecutableOperations, so it never applies cost tokens - both are needed to cover the queue.
                OperationApplier.ApplyTokenOperations(ops);
                await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));

                RuleOperation.InvokeWeaponAttack? attack =
                    ops.OfType<RuleOperation.InvokeWeaponAttack>().FirstOrDefault();
                if (attack == null) continue;

                if (attack.Weapon == null)
                {
                    // A unit-scoped rule using AttackWithThisWeapon: there is no "this weapon" to attack
                    // with, and picking one of the bearer's arbitrarily would invent a profile. The cost is
                    // already paid, which is deliberate - a silent free retry would hide the misauthoring.
                    RuleDiagnostics.WarnOnce($"strafe-scope:{offer.RuleName}",
                        $"'{offer.RuleName}' attacks with the carrying weapon but is attached to " +
                        $"{mover.Name} rather than to a weapon - nothing to attack with, so it is skipped. " +
                        "Attach it at weapon scope.");
                    continue;
                }

                _targetEnemy = enemy;
                _weapon = attack.Weapon;
                _defenderWoundsBefore = enemy.GetValue().RemainingWounds;
                GameContext.Log($"{mover.Name} used {offer.RuleName}, attacking " +
                    $"{enemy.GetValue().Name} with its {attack.Weapon.Name} while moving through.");

                await base.Enter(context);
                return;
            }

            await OnStrafeResolved.Activate(context);
        }

        /// <summary>
        /// "Pick one of them." One enemy crossed is the ordinary case and gets the yes/no it deserves - a
        /// one-option selection dialog is a worse question than the question it stands for. Several crossed
        /// get a cancellable pick, and no yes/no on top of it: an ability that already lets the player
        /// decline at the pick is not asked twice (the same ruling Dash's placement prompt follows).
        /// Returns null when the player declines.
        /// </summary>
        private async Task<DataBinding<UnitData>?> PickTargetCrossed(IUnit mover, AbilityOffer offer,
            List<DataBinding<UnitData>> crossed)
        {
            string weaponName = offer.Weapon?.Name ?? offer.RuleName;

            if (crossed.Count == 1)
            {
                var question = new YesNoRequest(mover.PlayerID,
                    $"Use {offer.RuleName}: {mover.Name} attacks {crossed[0].GetValue().Name} with its " +
                    $"{weaponName} while moving through?",
                    defaultAnswer: true);
                bool accepted = await GameContext.PlayerRequester
                    .RequestDecision<YesNoRequest, bool>(question);
                return accepted ? crossed[0] : null;
            }

            var options = new List<CancellableSelectionRequest<UnitData>.ValidOption>();
            foreach (DataBinding<UnitData> enemy in crossed)
            {
                options.Add(new CancellableSelectionRequest<UnitData>.ValidOption(
                    enemy, enemy.GetValue().Name));
            }

            var request = new CancellableSelectionRequest<UnitData>(mover.PlayerID,
                $"{offer.RuleName}: pick one unit moved through to attack with {weaponName}",
                options, System.Array.Empty<CancellableSelectionRequest<UnitData>.InvalidOption>());

            CancellableResult<DataBinding<UnitData>> reply = await GameContext.PlayerRequester
                .RequestDecision<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>(
                    request);

            return reply is Selected<DataBinding<UnitData>> selected ? selected.Value : null;
        }

        /// <summary>
        /// A strafe weapon is only ever usable by a unit that can move through enemies, and Strafing itself
        /// no longer grants that permission (the source rule never did - its carriers all have Aircraft or
        /// Flying). A bearer without it would carry a weapon it can never fire, which is exactly the kind of
        /// silent no-op #197 keeps finding, so say so once.
        /// </summary>
        private void WarnIfStrafeWeaponCannotReachAnEnemy(IUnit mover)
        {
            if (!StrafingRules.CarriesStrafeWeapon(mover)) return;
            if (MovementRuleQueries.CanMoveThroughEnemies(mover, GameContext.RuleEvaluator)) return;

            RuleDiagnostics.WarnOnce($"strafe-no-flyover:{mover.Name}",
                $"{mover.Name} carries a Strafing weapon but cannot move through enemy units, so the " +
                "attack can never trigger and the weapon can never be used. Give the unit Flying or " +
                "Aircraft (every corpus Strafing carrier has one).");
        }

        protected override ICombatMetadata GetNewChildContext(IMovementActionContext contextSelf)
        {
            CombatMetadata metadata = new CombatMetadata(GameContext, contextSelf.MovingUnit,
                _targetEnemy, _weapon!, weaponCount: 1, attackerMoved: true, isMelee: false);

            // The morale baseline, seeded the way every other cross-stage value in this chain travels.
            metadata.AddResult(new StrafeMoraleBaseline(_defenderWoundsBefore));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnStrafeResolved = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                // The shooting chain, minus RangeCheck and OcclusionCheck - see the class remarks.
                .AddChild(new BuildTargetListStage<ICombatMetadata>(GameContext, this), out var buildTargetList)
                .AddChild(new CoverCheckStage(GameContext, this), out var coverCheck)
                .AddChild(new DetermineHitRollStage<ICombatMetadata>(GameContext, this), out var determineHitRoll)
                .AddChild(new RollToHitStage<ICombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddChild(new ResolveStrafeMoraleStage(GameContext, this), out var resolveMorale)
                .AddSibling(nameof(OnStrafeResolved), OnStrafeResolved, out string strafeResolvedEvent)
                .Build();

            startingChild = buildTargetList;

            buildTargetList.BindNextStage(coverCheck)
                .BindNextStage(determineHitRoll)
                .BindNextStage(rollToHit)
                .BindNextStage(determineSaveRollsNeeded)
                .BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindNextStage(resolveMorale)
                .BindToEvent(strafeResolvedEvent);

            return dictionary;
        }
    }
}
