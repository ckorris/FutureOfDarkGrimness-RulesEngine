using System.Collections.Generic;
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

        private static bool IsAtOrigin(Position pos) => pos.x == 0f && pos.z == 0f;
    }
}
