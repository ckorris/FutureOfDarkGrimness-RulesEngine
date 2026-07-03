

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

        // --- Shape-aware overloads (#149/#150) -------------------------------------------------------
        // Exact base-to-base distance between two model base SHAPES, not just radii. The facing-aware
        // overloads measure oriented (e.g. rotated rectangular) bases correctly; the facing-less ones treat
        // both bases as forward (+Z) — exact for circles, axis-aligned for rectangles. The circle-radius
        // overloads above remain for paths with only a radius (terrain, LoS — see #150).

        public static float GetBaseToBaseDistanceInches_2D(Position positionA, Position positionB,
            IBaseShape baseShapeA, Float2 facingA, IBaseShape baseShapeB, Float2 facingB)
        {
            return BaseShapeGeometry.SurfaceGap2D(baseShapeA, positionA, facingA, baseShapeB, positionB, facingB);
        }

        public static float GetBaseToBaseDistanceInches_3D(Position positionA, Position positionB,
            IBaseShape baseShapeA, Float2 facingA, IBaseShape baseShapeB, Float2 facingB)
        {
            float distance2D = GetBaseToBaseDistanceInches_2D(positionA, positionB, baseShapeA, facingA, baseShapeB, facingB);
            float clampedDistance2D = Math.Max(0, distance2D);

            float verticalDistance = Math.Abs(positionA.y - positionB.y);

            return (float)Math.Sqrt(Math.Pow(clampedDistance2D, 2) + Math.Pow(verticalDistance, 2));
        }

        public static float GetBaseToBaseDistanceInches_2D(Position positionA, Position positionB,
            IBaseShape baseShapeA, IBaseShape baseShapeB)
        {
            return BaseShapeGeometry.SurfaceGap2D(baseShapeA, positionA, baseShapeB, positionB);
        }

        public static float GetBaseToBaseDistanceInches_3D(Position positionA, Position positionB,
            IBaseShape baseShapeA, IBaseShape baseShapeB)
        {
            float distance2D = GetBaseToBaseDistanceInches_2D(positionA, positionB, baseShapeA, baseShapeB);
            float clampedDistance2D = Math.Max(0, distance2D);

            float verticalDistance = Math.Abs(positionA.y - positionB.y);

            return (float)Math.Sqrt(Math.Pow(clampedDistance2D, 2) + Math.Pow(verticalDistance, 2));
        }
    }
}
