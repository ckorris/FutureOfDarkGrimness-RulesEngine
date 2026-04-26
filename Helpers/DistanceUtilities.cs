

using System;

namespace FDG
{
    public static class DistanceUtilities
    {
        public static float GetBaseToBaseDistanceInches_2D(Position positionA, Position positionB,
            float baseRadiusInchesA, float baseRadiusInchesB)
        {
            return Position.GetDistance2D(positionA, positionB)
                - baseRadiusInchesA
                - baseRadiusInchesB;
        }

        public static float GetBaseToBaseDistanceInches_3D(Position positionA, Position positionB,
            float baseRadiusInchesA, float baseRadiusInchesB)
        {
            //Radius doesn't affect vertical distance, so take that into account.
            float distance2D = GetBaseToBaseDistanceInches_2D(positionA, positionB, baseRadiusInchesA, baseRadiusInchesB);
            // Clamp to 0: overlapping models have base-to-base = 0, not a negative that inflates via Pythagoras.
            float clampedDistance2D = Math.Max(0, distance2D);

            float verticalDistance = Math.Abs(positionA.y - positionB.y);

            return (float)Math.Sqrt(Math.Pow(clampedDistance2D, 2) + Math.Pow(verticalDistance, 2));
        }
    }
}
