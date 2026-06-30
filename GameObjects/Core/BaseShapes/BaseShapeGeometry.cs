using System;

namespace FDG
{
    /// <summary>
    /// Exact base-to-base distance between two <see cref="IBaseShape"/>s (#149). This is the shape-aware
    /// half of the feature: model-to-model measurement (spacing, melee/charge reach, coherency) uses the
    /// real footprints. Known shape pairs (circle-circle, rect-rect, circle-rect, all axis-aligned) get
    /// closed-form geometry; any pair not yet handled falls back to the conservative bounding-circle gap
    /// (the extension seam — new shapes work immediately, then get exact pairs added; see WorkItems/150).
    /// </summary>
    public static class BaseShapeGeometry
    {
        /// <summary>
        /// Horizontal (X/Z) gap between the surfaces of base <paramref name="a"/> at <paramref name="posA"/>
        /// and base <paramref name="b"/> at <paramref name="posB"/>. Zero when the bases just touch,
        /// negative when they overlap (a penetration estimate), positive when apart. Vertical (Y) distance
        /// is ignored — combine via <see cref="DistanceUtilities.GetBaseToBaseDistanceInches_3D"/>.
        /// </summary>
        public static float SurfaceGap2D(IBaseShape a, Position posA, IBaseShape b, Position posB)
        {
            float dx = posB.x - posA.x;
            float dz = posB.z - posA.z;

            switch (a, b)
            {
                case (CircleBase ca, CircleBase cb):
                    return MathF.Sqrt(dx * dx + dz * dz) - ca.RadiusInches - cb.RadiusInches;

                case (RectangleBase ra, RectangleBase rb):
                    return RectRectGap(ra, rb, dx, dz);

                // (dx,dz) is the offset from a's centre to b's centre. CircleRectGap wants the offset from
                // the circle's centre to the rectangle's centre.
                case (CircleBase c, RectangleBase r):
                    return CircleRectGap(c, r, dx, dz);
                case (RectangleBase r, CircleBase c):
                    return CircleRectGap(c, r, -dx, -dz);

                default:
                    // Future shape pair not yet given exact geometry: conservative bounding-circle gap.
                    return MathF.Sqrt(dx * dx + dz * dz)
                        - a.BoundingRadiusInches - b.BoundingRadiusInches;
            }
        }

        /// <summary>
        /// Horizontal (X/Z) distance from <paramref name="point"/> to the nearest point on the surface of base
        /// <paramref name="shape"/> centred at <paramref name="center"/> and oriented by <paramref name="facing"/>
        /// (a yaw unit normal along the base's local +Z axis; a zero vector means forward/+Z). Zero when the point
        /// is on or inside the base, positive outside (#150). The shape-to-point form of <see cref="SurfaceGap2D"/>
        /// — used e.g. for objective seizure (objective centre to a model's base edge).
        /// </summary>
        public static float SurfaceDistanceToPoint2D(IBaseShape shape, Position center, Float2 facing, Position point)
        {
            float dx = point.x - center.x;
            float dz = point.z - center.z;

            switch (shape)
            {
                case CircleBase c:
                    return MathF.Max(0f, MathF.Sqrt(dx * dx + dz * dz) - c.RadiusInches);

                case RectangleBase r:
                {
                    // Rotate the offset into the base's local frame (local +Z = facing, local +X ⟂ facing), then
                    // it's a point-vs-axis-aligned-rectangle distance.
                    Float2 f = facing.X == 0f && facing.Y == 0f ? new Float2(0f, 1f) : facing;
                    float localZ = dx * f.X + dz * f.Y;   // along facing (height axis)
                    float localX = dx * f.Y - dz * f.X;   // along the perpendicular (width axis)
                    float qx = MathF.Max(0f, MathF.Abs(localX) - r.WidthInches * 0.5f);
                    float qz = MathF.Max(0f, MathF.Abs(localZ) - r.HeightInches * 0.5f);
                    return MathF.Sqrt(qx * qx + qz * qz);
                }

                default:
                    return MathF.Max(0f, MathF.Sqrt(dx * dx + dz * dz) - shape.BoundingRadiusInches);
            }
        }

        // Gap between two axis-aligned rectangles whose centres differ by (dx, dz).
        private static float RectRectGap(RectangleBase ra, RectangleBase rb, float dx, float dz)
        {
            float overlapX = MathF.Abs(dx) - (ra.WidthInches + rb.WidthInches) * 0.5f;
            float overlapZ = MathF.Abs(dz) - (ra.HeightInches + rb.HeightInches) * 0.5f;

            // Apart on at least one axis → Euclidean gap between the nearest edges/corners.
            if (overlapX > 0f || overlapZ > 0f)
            {
                float gx = MathF.Max(0f, overlapX);
                float gz = MathF.Max(0f, overlapZ);
                return MathF.Sqrt(gx * gx + gz * gz);
            }

            // Overlapping on both axes → least penetration depth (negative), mirroring circle-circle's sign.
            return MathF.Max(overlapX, overlapZ);
        }

        // Gap between a circle and an axis-aligned rectangle; (dx, dz) = rectangle centre − circle centre.
        private static float CircleRectGap(CircleBase circle, RectangleBase rect, float dx, float dz)
        {
            // Distance from the circle centre (origin) to the rectangle (centred at (dx, dz)).
            float qx = MathF.Max(0f, MathF.Abs(dx) - rect.WidthInches * 0.5f);
            float qz = MathF.Max(0f, MathF.Abs(dz) - rect.HeightInches * 0.5f);
            float centreToRect = MathF.Sqrt(qx * qx + qz * qz);
            return centreToRect - circle.RadiusInches;
        }
    }
}
