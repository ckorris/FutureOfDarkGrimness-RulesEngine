using System.Collections.Generic;
using System.Linq;
using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// Builds the model positions an <c>AttackBeat</c> animates between. Firing positions are the
    /// actual weapon-carrying models — matched against the firing weapon type — so a mixed unit
    /// (e.g. one rifle among five models) shows the shot from the right model, one entry per matching
    /// weapon instance. Target positions are the defender's alive, on-table models.
    /// Models held in reserve (Ambush) sit at the origin and are excluded.
    /// </summary>
    internal static class AttackBeatPositions
    {
        public static List<Position> FiringModels(DataBinding<UnitData> unit, IWeapon weaponType)
        {
            var comparer = new WeaponComparer();
            var positions = new List<Position>();
            foreach (IModel model in unit.GetValue().Models)
            {
                if (!model.GetIsAlive() || IsAtOrigin(model.Position)) continue;
                foreach (Weapon weapon in model.Weapons)
                {
                    if (comparer.Equals(weapon, weaponType))
                        positions.Add(model.Position);
                }
            }
            return positions;
        }

        public static List<Position> AlivePlaced(DataBinding<UnitData> unit)
        {
            var positions = new List<Position>();
            foreach (IModel model in unit.GetValue().Models)
            {
                if (!model.GetIsAlive() || IsAtOrigin(model.Position)) continue;
                positions.Add(model.Position);
            }
            return positions;
        }

        /// <summary>
        /// The defender's alive, on-table models that at least one firing model can actually see
        /// (terrain + intervening enemy bases considered, same as the real LoS gate). Keeps the shot
        /// animation honest — we never draw a tracer at a model the unit couldn't legally target, even
        /// though the rules resolve damage at the unit level. Falls back to all alive models if the
        /// filter would be empty (the occlusion stage already guaranteed the unit can see *something*,
        /// and an empty target set would suppress the animation entirely).
        /// </summary>
        public static List<Position> VisibleTargets(ITableState tableState,
            DataBinding<UnitData> attackingUnit, DataBinding<UnitData> defendingUnit, IWeapon weaponType)
        {
            List<Position> firing = FiringModels(attackingUnit, weaponType);
            List<Position> targets = AlivePlaced(defendingUnit);
            if (firing.Count == 0 || targets.Count == 0) return targets;

            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(tableState, attackingUnit, defendingUnit);
            var terrain = tableState.Terrain.Objects.Concat(modelBlockers).ToList();

            var visible = new List<Position>();
            foreach (Position target in targets)
            {
                foreach (Position shooter in firing)
                {
                    if (LineOfSightUtilities.HasLineOfSight(shooter, target, terrain))
                    {
                        visible.Add(target);
                        break;
                    }
                }
            }
            return visible.Count > 0 ? visible : targets;
        }

        private static bool IsAtOrigin(Position pos) => pos.x == 0f && pos.z == 0f;
    }
}
