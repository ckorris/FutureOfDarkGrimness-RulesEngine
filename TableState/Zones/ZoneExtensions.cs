namespace FDG
{
    /// <summary>
    /// Generic helpers usable on any <see cref="IZone"/>. The two important ones are
    /// <see cref="Primitives"/> (flattens composites) and <see cref="GetAABB"/>
    /// (axis-aligned bounding box). Most code that used to switch on shape type
    /// can be rewritten in terms of these and Just Work for composites.
    /// </summary>
    public static class ZoneExtensions
    {
        /// <summary>
        /// Walks the zone tree and yields the leaf primitives (non-composite zones).
        /// Primitive zones yield themselves; composites yield each part's primitives
        /// recursively.
        /// </summary>
        public static IEnumerable<IZone> Primitives(this IZone zone)
        {
            if (zone is CompositeZone composite)
            {
                foreach (var part in composite.Parts)
                    foreach (var leaf in part.Primitives())
                        yield return leaf;
            }
            else
            {
                yield return zone;
            }
        }

        /// <summary>Axis-aligned bounding box of the union of all primitives in the zone.</summary>
        public static (float minX, float maxX, float minY, float maxY) GetAABB(this IZone zone)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            bool any = false;
            foreach (var prim in zone.Primitives())
            {
                (float lx, float hx, float ly, float hy) = PrimitiveAABB(prim);
                if (lx < minX) minX = lx;
                if (hx > maxX) maxX = hx;
                if (ly < minY) minY = ly;
                if (hy > maxY) maxY = hy;
                any = true;
            }
            if (!any)
                throw new InvalidOperationException("GetAABB called on an empty zone (no primitives).");
            return (minX, maxX, minY, maxY);
        }

        public static Float2 GetAABBCenter(this IZone zone)
        {
            (float lx, float hx, float ly, float hy) = zone.GetAABB();
            return new Float2((lx + hx) * 0.5f, (ly + hy) * 0.5f);
        }

        private static (float, float, float, float) PrimitiveAABB(IZone prim) => prim switch
        {
            RectangularZone r => (r.Left, r.Right, r.Bottom, r.Top),
            CircularZone c => (c.Center.X - c.Radius, c.Center.X + c.Radius, c.Center.Y - c.Radius, c.Center.Y + c.Radius),
            _ => throw new NotSupportedException($"Unsupported primitive zone for AABB: {prim.GetType().Name}.")
        };
    }
}
