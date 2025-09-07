
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

            foreach(IModel modelA in unitA.Models)
            {
                foreach(IModel modelB in unitB.Models)
                {
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
    }
}
