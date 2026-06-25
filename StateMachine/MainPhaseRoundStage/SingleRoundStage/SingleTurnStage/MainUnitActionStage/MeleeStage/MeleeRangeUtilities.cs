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
        /// <summary>The single definition of the melee-engagement cylinder: true if both models are alive
        /// and within <see cref="GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL"/> (2") base-to-base
        /// horizontally AND within <see cref="GameWideConstants.MELEE_RANGE_INCHES_VERTICAL"/> (4")
        /// vertically. Every melee-range gate (charge availability, defender eligibility, strike range)
        /// flows through here so they can't disagree.</summary>
        public static bool AreModelsInMeleeRange(IModel model, IModel enemy)
        {
            if (!model.GetIsAlive() || !enemy.GetIsAlive()) return false;

            float horizontal = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                model.Position, enemy.Position, model.BaseShape, enemy.BaseShape);
            if (horizontal > GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL) return false;

            float vertical = Position.GetVerticalDistance(model.Position, enemy.Position);
            if (vertical > GameWideConstants.MELEE_RANGE_INCHES_VERTICAL) return false;

            return true;
        }

        /// <summary>True if any living model in <paramref name="unit"/> is within melee range of any living
        /// model in <paramref name="enemyUnit"/>. The unit-level gate shared by the Charge-availability
        /// check (<c>ChooseActionStage.GetCanCharge</c>) and defender eligibility
        /// (<c>ChooseMeleeDefenderStage</c>) — both formerly applied only the horizontal half (#022).</summary>
        public static bool AreUnitsInMeleeRange(IUnit unit, IUnit enemyUnit)
        {
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;

                foreach (IModel enemy in enemyUnit.Models)
                {
                    if (AreModelsInMeleeRange(model, enemy)) return true;
                }
            }
            return false;
        }

        /// <summary>True if <paramref name="model"/> is alive and within melee range of any live model in
        /// <paramref name="enemyModels"/>.</summary>
        public static bool IsModelInMeleeRange(ModelData model, IReadOnlyList<DataBinding<ModelData>> enemyModels)
        {
            if (!((IModel)model).GetIsAlive()) return false;

            foreach (DataBinding<ModelData> enemyBinding in enemyModels)
            {
                if (AreModelsInMeleeRange(model, enemyBinding.GetValue())) return true;
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
