using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// Determines which models are within melee striking range of an enemy unit, after a charge
    /// and pile-in have settled positions. GF v3.5.1: a model may strike (or strike back) only if
    /// it is within <see cref="GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL"/> (2") base-to-base
    /// horizontally AND within <see cref="GameWideConstants.MELEE_RANGE_INCHES_VERTICAL"/> (4")
    /// vertically of an enemy model.
    /// </summary>
    public static class MeleeRangeUtilities
    {
        /// <summary>True if <paramref name="model"/> is alive and within melee range of any live model in
        /// <paramref name="enemyModels"/>.</summary>
        public static bool IsModelInMeleeRange(ModelData model, IReadOnlyList<DataBinding<ModelData>> enemyModels)
        {
            if (!((IModel)model).GetIsAlive()) return false;

            foreach (DataBinding<ModelData> enemyBinding in enemyModels)
            {
                ModelData enemy = enemyBinding.GetValue();
                if (!((IModel)enemy).GetIsAlive()) continue;

                float horizontal = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                    model.Position, enemy.Position, model.BaseRadiusInches, enemy.BaseRadiusInches);
                if (horizontal > GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL) continue;

                float vertical = Position.GetVerticalDistance(model.Position, enemy.Position);
                if (vertical > GameWideConstants.MELEE_RANGE_INCHES_VERTICAL) continue;

                return true;
            }
            return false;
        }

        /// <summary>Returns the subset of <paramref name="models"/> that are in melee range of any model in
        /// <paramref name="enemyModels"/>. Bindings are preserved so callers can rebuild weapon pools from them.</summary>
        public static List<DataBinding<ModelData>> GetModelsInMeleeRange(
            IReadOnlyList<DataBinding<ModelData>> models,
            IReadOnlyList<DataBinding<ModelData>> enemyModels)
        {
            List<DataBinding<ModelData>> inRange = new List<DataBinding<ModelData>>();
            foreach (DataBinding<ModelData> binding in models)
            {
                if (IsModelInMeleeRange(binding.GetValue(), enemyModels))
                {
                    inRange.Add(binding);
                }
            }
            return inRange;
        }

        /// <summary>Flattens the live melee weapons carried by <paramref name="models"/> into one list,
        /// for building the in-range attack pool.</summary>
        public static List<Weapon> GetMeleeWeaponsFromModels(IReadOnlyList<DataBinding<ModelData>> models)
        {
            List<Weapon> weapons = new List<Weapon>();
            foreach (DataBinding<ModelData> binding in models)
            {
                ModelData model = binding.GetValue();
                if (!((IModel)model).GetIsAlive()) continue;

                foreach (Weapon weapon in model.Weapons)
                {
                    if (weapon.IsMelee())
                    {
                        weapons.Add(weapon);
                    }
                }
            }
            return weapons;
        }
    }
}
