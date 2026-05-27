namespace FDG
{
    /// <summary>
    /// Generic helpers usable on any <see cref="IZone"/>. The two important ones are
    /// <see cref="Primitives"/> (flattens composites and rotated wrappers down to leaf
    /// primitives — rectangles, circles, and rotated rectangles) and
    /// <see cref="GetAABB"/> (axis-aligned bounding box of the union).
    /// </summary>
    public static class ZoneExtensions
    {
        /// <summary>
        /// Walks the zone tree and yields the leaf primitives. Composites are
        /// flattened recursively. Rotated wrappers are pushed down: a rotated composite
        /// becomes a sequence of rotated leaves (all sharing the wrapper's pivot).
        /// Rotated circles collapse to translated <see cref="CircularZone"/> leaves
        /// since circles are rotation-invariant about their own center.
        /// </summary>
        public static IEnumerable<IZone> Primitives(this IZone zone)
        {
            switch (zone)
            {
                case CompositeZone c:
                    foreach (var part in c.Parts)
                        foreach (var leaf in part.Primitives())
                            yield return leaf;
                    break;

                case RotatedZoneWrapper w:
                    foreach (var leaf in w.Inner.Primitives())
                    {
                        switch (leaf)
                        {
                            case CircularZone cl:
                                Float2 newCenter = RotateAround(cl.Center, w.Pivot, w.AngleDegrees);
                                yield return new CircularZone(newCenter, cl.Radius);
                                break;

                            case RectangularZone rl:
                                yield return new RotatedZoneWrapper(rl, w.AngleDegrees, w.Pivot);
                                break;

                            case RotatedZoneWrapper inner:
                                // Compose: rotate the inner pivot by outer rotation, sum angles.
                                Float2 composedPivot = RotateAround(inner.Pivot, w.Pivot, w.AngleDegrees);
                                yield return new RotatedZoneWrapper(
                                    inner.Inner, inner.AngleDegrees + w.AngleDegrees, composedPivot);
                                break;

                            default:
                                throw new NotSupportedException($"Unhandled leaf type under RotatedZoneWrapper: {leaf.GetType().Name}.");
                        }
                    }
                    break;

                default:
                    yield return zone;
                    break;
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

        /// <summary>Rotates <paramref name="point"/> by <paramref name="angleDegrees"/> around <paramref name="pivot"/>.</summary>
        public static Float2 RotateAround(Float2 point, Float2 pivot, float angleDegrees)
        {
            if (angleDegrees == 0f) return point;
            float rad = angleDegrees * MathF.PI / 180f;
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            float dx = point.X - pivot.X;
            float dy = point.Y - pivot.Y;
            return new Float2(
                pivot.X + dx * cos - dy * sin,
                pivot.Y + dx * sin + dy * cos);
        }

        private static (float, float, float, float) PrimitiveAABB(IZone prim)
        {
            switch (prim)
            {
                case RectangularZone r:
                    return (r.Left, r.Right, r.Bottom, r.Top);

                case CircularZone c:
                    return (c.Center.X - c.Radius, c.Center.X + c.Radius,
                            c.Center.Y - c.Radius, c.Center.Y + c.Radius);

                case RotatedZoneWrapper w when w.Inner is RectangularZone r:
                    return RotatedRectAABB(r, w.AngleDegrees, w.Pivot);

                default:
                    throw new NotSupportedException($"Unsupported primitive zone for AABB: {prim.GetType().Name}.");
            }
        }

        private static (float, float, float, float) RotatedRectAABB(RectangularZone r, float angleDegrees, Float2 pivot)
        {
            Float2[] corners =
            {
                new Float2(r.Left, r.Bottom),
                new Float2(r.Right, r.Bottom),
                new Float2(r.Right, r.Top),
                new Float2(r.Left, r.Top),
            };

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (var corner in corners)
            {
                Float2 rc = RotateAround(corner, pivot, angleDegrees);
                if (rc.X < minX) minX = rc.X;
                if (rc.X > maxX) maxX = rc.X;
                if (rc.Y < minY) minY = rc.Y;
                if (rc.Y > maxY) maxY = rc.Y;
            }
            return (minX, maxX, minY, maxY);
        }
    }
}
