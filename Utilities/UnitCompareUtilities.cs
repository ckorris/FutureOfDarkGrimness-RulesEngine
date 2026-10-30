
namespace FDG.Utilities
{
    public static class UnitCompareUtilities
    {
        public static float MinDistanceBetweenUnits(IUnit unitA, IUnit unitB, out IModel? closestA, out IModel? closestB, 
            bool includeVertical)
        {
            float closestDistance = float.PositiveInfinity;

            closestA = null;
            closestB = null;

            if (unitA.Models.Count == 0 || unitB.Models.Count == 0)
            {
                return closestDistance;
            }

            // Distance between units is measured only between living models — a corpse still sitting where
            // it fell must not make a unit appear in melee/shooting range. Callers (charge/defender-selection
            // gates, shooting range bands) all want range to a target that can actually be engaged. A unit
            // with no living models reports +infinity (no pair found), which the callers read as "out of range".
            foreach(IModel modelA in unitA.Models)
            {
                if (!modelA.GetIsAlive()) continue;

                foreach(IModel modelB in unitB.Models)
                {
                    if (!modelB.GetIsAlive()) continue;

                    float distance = includeVertical
                        ? modelA.BaseDistanceToOtherModel_3D(modelB)
                        : modelA.BaseDistanceToOtherModel_2D(modelB);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestA = modelA;
                        closestB = modelB;
                    }
                }
            }

            return closestDistance;
        }

        /// <summary>
        /// #421: the closest base-to-base distance from one model to any LIVING model of <paramref name="unit"/> -
        /// <see cref="MinDistanceBetweenUnits"/> with one side narrowed to a single model, for rules measured from
        /// one model of a unit (the 3.6 Hero's "units within 6\" of friendly Heroes"). Same measure, same
        /// +infinity for a unit with no living models.
        /// </summary>
        public static float MinDistanceFromModelToUnit(IModel model, IUnit unit, bool includeVertical)
        {
            float closestDistance = float.PositiveInfinity;
            foreach (IModel other in unit.Models)
            {
                if (!other.GetIsAlive()) continue;

                float distance = includeVertical
                    ? model.BaseDistanceToOtherModel_3D(other)
                    : model.BaseDistanceToOtherModel_2D(other);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                }
            }

            return closestDistance;
        }
    }
}
