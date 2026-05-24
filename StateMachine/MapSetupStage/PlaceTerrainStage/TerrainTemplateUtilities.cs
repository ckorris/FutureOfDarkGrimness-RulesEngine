namespace FDG.Stages
{
    /// <summary>
    /// Helpers for translating a terrain template to a new world-space center.
    /// Center = AABB center so composites work the same as primitives.
    /// </summary>
    public static class TerrainTemplateUtilities
    {
        public static Float2 GetCenter(IZone shape) => shape.GetAABBCenter();

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
            _ => throw new NotSupportedException($"Unsupported zone type for terrain translation: {z.GetType().Name}.")
        };
    }
}
