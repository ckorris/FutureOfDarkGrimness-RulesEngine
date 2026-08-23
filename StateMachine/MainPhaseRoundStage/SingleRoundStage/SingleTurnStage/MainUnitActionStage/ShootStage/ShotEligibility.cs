using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// The one answer to "which defender model can this shooter actually hit?" — shared by the rules,
    /// the attack animation and the targeting previews so the three can never disagree.
    ///
    /// <para>Two halves, both of which were being hand-rolled at every call site:</para>
    /// <list type="bullet">
    /// <item><see cref="BuildBlockers"/> — the terrain snapshot concatenated with
    /// <see cref="LineOfSightUtilities.BuildModelBlockers"/> (which units are transparent depends on
    /// the #384 see-through-allies setting; off-table models never block). Every sight check in the
    /// shooting flow needs exactly this list, and forgetting the model half silently lets shots pass
    /// through a crowd.</item>
    /// <item><see cref="NearestVisibleModel"/> — the nearest living defender the shooter can both SEE
    /// and reach, by the engine's base-to-base 3D metric.</item>
    /// </list>
    ///
    /// <para>Written because a preview that re-derives the test drifts from the rules that enforce it:
    /// the shoot panel's fire lines picked the nearest defender by raw distance and drew straight
    /// through a wall, while the volley itself (correctly) resolved against a model it could see.</para>
    /// </summary>
    public static class ShotEligibility
    {
        /// <summary>
        /// The full blocker list for an (attacker, defender) sight test: table terrain plus the model
        /// blockers <see cref="LineOfSightUtilities.BuildModelBlockers"/> produces under the game's
        /// #384 <paramref name="seeThroughFriendlyUnits"/> setting.
        /// </summary>
        public static IReadOnlyList<ITerrain> BuildBlockers(ITableState tableState,
            IUnit attackingUnit, IUnit defendingUnit, bool seeThroughFriendlyUnits)
        {
            List<ITerrain> modelBlockers = LineOfSightUtilities.BuildModelBlockers(
                tableState, attackingUnit, defendingUnit, seeThroughFriendlyUnits);
            var blockers = new List<ITerrain>(tableState.Terrain.Objects.Count() + modelBlockers.Count);
            blockers.AddRange(tableState.Terrain.Objects);
            blockers.AddRange(modelBlockers);
            return blockers;
        }

        /// <inheritdoc cref="BuildBlockers(ITableState, IUnit, IUnit, bool)"/>
        public static IReadOnlyList<ITerrain> BuildBlockers(ITableState tableState,
            DataBinding<UnitData> attackingUnit, DataBinding<UnitData> defendingUnit,
            bool seeThroughFriendlyUnits)
            => BuildBlockers(tableState, attackingUnit.GetValue(), defendingUnit.GetValue(),
                seeThroughFriendlyUnits);

        /// <summary>
        /// The nearest model in <paramref name="targets"/> that a shooter at <paramref name="from"/> can
        /// see and reach, or null when none qualifies. Distance is the engine's base-to-base 3D metric —
        /// the same one the range check rolls on.
        /// </summary>
        /// <param name="blockers">Sight blockers from <see cref="BuildBlockers"/>. <b>Null means the
        /// shot ignores line of sight</b> (Indirect), so only range gates it. Callers derive that from
        /// <see cref="Rules.Dispatch.SightRuleQueries.IgnoresTerrain"/>; Takedown used to qualify and no
        /// longer does (#314 - its rule text grants no LoS bypass), so this parameter's value changed
        /// for snipers even though the logic here did not.</param>
        /// <param name="maxRangeInches">Effective range, already folded through any #102 range rules.
        /// Omit for "range is not my question" — a caller that already knows the shot is legal and only
        /// needs to know WHICH model it is aimed at.</param>
        /// <remarks>
        /// Callers that hold a set of shooters already known to be able to fire (the targeting request's
        /// <c>modelsThatCanShoot</c>) may omit the range: the nearest VISIBLE model is necessarily within
        /// range for such a shooter, since every other visible model is farther away.
        /// </remarks>
        public static IModel? NearestVisibleModel(Position from, IBaseShape fromShape, Float2 fromFacing,
            IReadOnlyList<IModel> targets, IReadOnlyList<ITerrain>? blockers,
            float maxRangeInches = float.PositiveInfinity)
        {
            IModel? best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (IModel target in targets)
            {
                if (!target.GetIsAlive()) continue;
                // Never placed / in reserve / embarked — parked at the origin, not on the table.
                if (target.Position.x == 0f && target.Position.z == 0f) continue;

                float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(from, target.Position,
                    fromShape, fromFacing, target.BaseShape, target.Facing);
                if (distance > maxRangeInches) continue;
                if (distance >= bestDistance) continue;
                if (blockers != null && !LineOfSightUtilities.HasLineOfSight(from, target.Position, blockers))
                    continue;

                bestDistance = distance;
                best = target;
            }
            return best;
        }

        /// <summary>
        /// Whether a shooter at <paramref name="from"/> can hit ANY of <paramref name="targets"/> — the
        /// unit-level "can this weapon shoot that unit" gate, phrased as the model-level question it
        /// actually is. Mirrors <c>ChooseRangedAttackStage.CanWeaponShootAtUnit</c>.
        /// </summary>
        public static bool CanHitAny(Position from, IBaseShape fromShape, Float2 fromFacing,
            IReadOnlyList<IModel> targets, IReadOnlyList<ITerrain>? blockers, float maxRangeInches)
            => NearestVisibleModel(from, fromShape, fromFacing, targets, blockers, maxRangeInches) != null;

        /// <summary>
        /// Whether ANY living, placed model of <paramref name="attackingUnit"/> can see ANY living,
        /// placed model of <paramref name="defendingUnit"/> — the unit-level occlusion gate
        /// (<see cref="OcclusionCheckStage"/>), built on the same per-model sight test the targeting
        /// previews and the attack animation use, so the gate cannot pass a shot no living model was
        /// offered (#385: it used to iterate raw model bindings, letting a dead model keep a sight
        /// line alive). Range is deliberately not asked — occlusion is a pure sight question; the
        /// range gate already ran at targeting.
        /// </summary>
        public static bool UnitSeesUnit(IUnit attackingUnit, IUnit defendingUnit,
            IReadOnlyList<ITerrain> blockers)
        {
            foreach (IModel shooter in attackingUnit.Models)
            {
                if (!shooter.GetIsAlive()) continue;
                if (shooter.Position.x == 0f && shooter.Position.z == 0f) continue;
                // NearestVisibleModel applies the same alive+placed filter to the defender side.
                if (NearestVisibleModel(shooter.Position, shooter.BaseShape, shooter.Facing,
                        defendingUnit.Models, blockers) != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc cref="UnitSeesUnit(IUnit, IUnit, IReadOnlyList{ITerrain})"/>
        public static bool UnitSeesUnit(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> defendingUnit, IReadOnlyList<ITerrain> blockers)
            => UnitSeesUnit(attackingUnit.GetValue(), defendingUnit.GetValue(), blockers);
    }
}
