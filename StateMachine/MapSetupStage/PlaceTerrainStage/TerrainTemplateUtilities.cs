namespace FDG.Stages
{
    /// <summary>
    /// Helpers for materializing a terrain template at a chosen world position and
    /// rotation. The pipeline is: <see cref="Rotate"/> the template around its own
    /// AABB center, then <see cref="TranslateToCenter"/> so its rotated AABB center
    /// lands at the user-clicked point.
    /// </summary>
    public static class TerrainTemplateUtilities
    {
        public static Float2 GetCenter(IZone shape) => shape.GetAABBCenter();

        /// <summary>
        /// Wraps <paramref name="shape"/> with a rotation by <paramref name="angleDegrees"/>
        /// around the shape's AABB center. Returns <paramref name="shape"/> unchanged for
        /// angle == 0.
        /// </summary>
        public static IZone Rotate(IZone shape, float angleDegrees)
        {
            if (angleDegrees == 0f) return shape;
            Float2 pivot = shape.GetAABBCenter();
            return new RotatedZoneWrapper(shape, angleDegrees, pivot);
        }

        public static IZone TranslateToCenter(IZone template, Float2 newCenter)
        {
            Float2 currentCenter = template.GetAABBCenter();
            float dx = newCenter.X - currentCenter.X;
            float dy = newCenter.Y - currentCenter.Y;
            return TranslateBy(template, dx, dy);
        }

        private static IZone TranslateBy(IZone z, float dx, float dy) => z switch
        {
            RectangularZone r => new RectangularZone(r.Left + dx, r.Right + dx, r.Bottom + dy, r.Top + dy),
            CircularZone c => new CircularZone(new Float2(c.Center.X + dx, c.Center.Y + dy), c.Radius),
            CompositeZone comp => new CompositeZone(comp.Parts.Select(p => TranslateBy(p, dx, dy)).ToList()),
            RotatedZoneWrapper w => new RotatedZoneWrapper(
                TranslateBy(w.Inner, dx, dy),
                w.AngleDegrees,
                new Float2(w.Pivot.X + dx, w.Pivot.Y + dy)),
            _ => throw new NotSupportedException($"Unsupported zone type for terrain translation: {z.GetType().Name}.")
        };
    }
}
