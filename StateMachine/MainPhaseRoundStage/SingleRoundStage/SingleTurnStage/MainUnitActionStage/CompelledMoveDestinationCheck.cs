using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 Instinctive slice 3 - the RESOLVER-side half of the compelled manual move: does this
    /// candidate destination end able to attack? Built from what the movement request already carries
    /// (<see cref="DefineMovementPathRequest.WeaponSightProfiles"/> for per-weapon LoS-ignore,
    /// <see cref="DefineMovementPathRequest.WeaponRangeOverrides"/> for rule-modified ranges, per-model
    /// budgets) plus <see cref="ITableState"/> geometry, because resolvers have no
    /// <c>IGameContext</c> - this is the same request-data channel every move-preview overlay uses.
    ///
    /// <para>Two knowing approximations, both graceful: intervening MODEL silhouettes are not counted as
    /// LoS blockers (terrain is), and rule effects not expressible in the request data are absent. Either
    /// can let a not-quite-compliant move through - never block a compliant one on range grounds - and a
    /// through-move simply lands where the post-move action menu re-evaluates the compulsion with the
    /// authoritative gates (the stage is deliberately graceful about this, see
    /// <c>DefineMovementPathRequest.MustEndAbleToAttackRule</c>).</para>
    /// </summary>
    public static class CompelledMoveDestinationCheck
    {
        /// <summary>
        /// True when the candidate <paramref name="entries"/> end within melee range of some enemy, or -
        /// when every model stayed inside its own Advance budget - with some enemy model in range and
        /// line of sight of some ranged weapon. Mirrors the "shoot after Advance OR charge from contact"
        /// disjunction the post-move menu applies.
        /// </summary>
        public static bool EndsAbleToAttack(ITableState tableState, DefineMovementPathRequest request,
            IReadOnlyList<ModelMoveEntry> entries)
        {
            PlayerID mover = request.UnitDataBinding.GetValue().PlayerID;
            List<IUnit> enemies = tableState.Units.Objects
                .Where(u => !ITeamExtensions.AreAllied(tableState.Teams.Objects, mover, u.PlayerID)
                            && u.GetIsOnBattlefield()
                            && u.Models.Any(m => m.GetIsAlive()))
                .ToList();
            if (enemies.Count == 0) return false;

            if (EndsInMeleeRange(entries, enemies)) return true;

            return StaysAnAdvance(request, entries) && EndsWithAFireableTarget(tableState, request, entries, enemies);
        }

        // The charge half: any moved model's projected position inside the melee cylinder of any living
        // enemy model. Aircraft are excluded exactly as ChooseActionStage.GetCanCharge excludes them.
        private static bool EndsInMeleeRange(IReadOnlyList<ModelMoveEntry> entries, List<IUnit> enemies)
        {
            foreach (ModelMoveEntry entry in entries)
            {
                IModel model = entry.Model.GetValue();
                if (!model.GetIsAlive()) continue;
                Position at = entry.Positions[^1];

                foreach (IUnit enemy in enemies)
                {
                    if (Rules.Dispatch.AircraftRules.IsAircraft(enemy)) continue;
                    foreach (IModel enemyModel in enemy.Models)
                    {
                        if (!enemyModel.GetIsAlive()) continue;

                        float horizontal = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                            at, enemyModel.Position, model.BaseShape, model.Facing,
                            enemyModel.BaseShape, enemyModel.Facing);
                        if (horizontal > GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL) continue;

                        if (Position.GetVerticalDistance(at, enemyModel.Position)
                            <= GameWideConstants.MELEE_RANGE_INCHES_VERTICAL)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // "Having Advanced": every model within its OWN advance budget (#093), the same per-model
        // number the request carried for the preview caps.
        private static bool StaysAnAdvance(DefineMovementPathRequest request, IReadOnlyList<ModelMoveEntry> entries)
        {
            foreach (ModelMoveEntry entry in entries)
            {
                (float advance, _, _) = request.BudgetFor(entry.Model.GetValue().ID);
                float travelled = 0f;
                Position previous = entry.Model.GetValue().Position;
                foreach (Position waypoint in entry.Positions)
                {
                    travelled += Position.GetDistance3D(previous, waypoint);
                    previous = waypoint;
                }
                if (!travelled.LessThanOrAlmostEqual(advance)) return false;
            }
            return true;
        }

        // The shoot half: any (projected model, ranged weapon, living enemy model) triple in effective
        // range with line of sight. Effective range honours the request's per-(weapon, enemy) overrides;
        // sight honours the per-weapon LoS-ignore profile; terrain blocks, model silhouettes knowingly
        // don't (see the class doc).
        private static bool EndsWithAFireableTarget(ITableState tableState, DefineMovementPathRequest request,
            IReadOnlyList<ModelMoveEntry> entries, List<IUnit> enemies)
        {
            IReadOnlyList<ITerrain> terrain = tableState.Terrain.Objects.ToList();

            foreach (ModelMoveEntry entry in entries)
            {
                IModel model = entry.Model.GetValue();
                if (!model.GetIsAlive()) continue;
                Position at = entry.Positions[^1];

                foreach (Weapon weapon in model.Weapons)
                {
                    if (weapon.RangeInches <= 0f) continue;

                    bool ignoresLineOfSight = request.WeaponSightProfiles
                        .Any(p => p.Weapon.Name == weapon.Name && p.IgnoresTerrain);

                    foreach (IUnit enemy in enemies)
                    {
                        float effectiveRange = request.WeaponRangeOverrides
                            .FirstOrDefault(o => o.WeaponName == weapon.Name && o.EnemyUnitId.Equals(enemy.ID))
                            ?.EffectiveRangeInches ?? weapon.RangeInches;

                        foreach (IModel enemyModel in enemy.Models)
                        {
                            if (!enemyModel.GetIsAlive()) continue;

                            float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                                at, enemyModel.Position, model.BaseShape, model.Facing,
                                enemyModel.BaseShape, enemyModel.Facing);
                            if (distance > effectiveRange) continue;

                            if (ignoresLineOfSight
                                || LineOfSightUtilities.HasLineOfSight(at, enemyModel.Position, terrain))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
