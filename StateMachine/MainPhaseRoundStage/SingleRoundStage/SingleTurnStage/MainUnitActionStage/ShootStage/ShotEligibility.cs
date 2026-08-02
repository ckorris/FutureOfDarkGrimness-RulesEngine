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
    /// <see cref="LineOfSightUtilities.BuildModelBlockers"/> (intervening ENEMY bases block; allied and
    /// off-table models don't). Every sight check in the shooting flow needs exactly this list, and
    /// forgetting the model half silently lets shots pass through a crowd.</item>
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
        /// The full blocker list for an (attacker, defender) sight test: table terrain plus every model
        /// that is neither the attacker's ally nor part of the defending unit.
        /// </summary>
        public static IReadOnlyList<ITerrain> BuildBlockers(ITableState tableState,
            IUnit attackingUnit, IUnit defendingUnit)
        {
            List<ITerrain> modelBlockers =
                LineOfSightUtilities.BuildModelBlockers(tableState, attackingUnit, defendingUnit);
            var blockers = new List<ITerrain>(tableState.Terrain.Objects.Count() + modelBlockers.Count);
            blockers.AddRange(tableState.Terrain.Objects);
            blockers.AddRange(modelBlockers);
            return blockers;
        }

        /// <inheritdoc cref="BuildBlockers(ITableState, IUnit, IUnit)"/>
        public static IReadOnlyList<ITerrain> BuildBlockers(ITableState tableState,
            DataBinding<UnitData> attackingUnit, DataBinding<UnitData> defendingUnit)
            => BuildBlockers(tableState, attackingUnit.GetValue(), defendingUnit.GetValue());

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
    }
}
