namespace FDG.Stages
{
    /// <summary>
    /// Helpers for translating a terrain template (an <see cref="IZone"/> stored with
    /// its design-time anchor coordinates) to a new world-space center, so the player
    /// can place a copy of the template anywhere on the table without those design
    /// coordinates leaking through.
    /// </summary>
    public static class TerrainTemplateUtilities
    {
        /// <summary>The point that <see cref="TranslateToCenter"/> treats as a shape's center.</summary>
        public static Float2 GetCenter(IZone shape) => shape switch
        {
            RectangularZone r => new Float2((r.Left + r.Right) * 0.5f, (r.Bottom + r.Top) * 0.5f),
            CircularZone c => c.Center,
            _ => throw new NotSupportedException($"Unsupported zone type for terrain center: {shape.GetType().Name}.")
        };

        /// <summary>Returns a new <see cref="IZone"/> with the same dimensions as <paramref name="template"/>, centered at <paramref name="newCenter"/>.</summary>
        public static IZone TranslateToCenter(IZone template, Float2 newCenter)
        {
            Float2 currentCenter = GetCenter(template);
            float dx = newCenter.X - currentCenter.X;
            float dy = newCenter.Y - currentCenter.Y;

            return template switch
            {
                RectangularZone r => new RectangularZone(r.Left + dx, r.Right + dx, r.Bottom + dy, r.Top + dy),
                CircularZone c => new CircularZone(newCenter, c.Radius),
                _ => throw new NotSupportedException($"Unsupported zone type for terrain translation: {template.GetType().Name}.")
            };
        }
    }
}
