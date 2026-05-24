namespace FDG.Stages
{
    public enum TerrainPlacementValidity
    {
        Valid,
        OutOfBounds,
        OverlapsExistingTerrain,
    }

    /// <summary>
    /// Pure-function checks for whether a candidate terrain footprint is a legal
    /// placement. Per #002 Decisions: footprint must be fully inside the table, and
    /// must not overlap (touching counts as overlap; a small <see cref="GapMarginInches"/>
    /// guards against float drift). All shape pairs are reduced to per-primitive
    /// checks via <see cref="ZoneExtensions.Primitives"/>, so composites (L-shapes,
    /// T-shapes, etc.) work transparently.
    /// </summary>
    public static class TerrainPlacementValidator
    {
        public const float GapMarginInches = 0.01f;

        public static TerrainPlacementValidity Check(
            IZone candidate,
            float tableWidthInches,
            float tableHeightInches,
            IEnumerable<ITerrain> existing)
        {
            if (!IsInsideTable(candidate, tableWidthInches, tableHeightInches))
                return TerrainPlacementValidity.OutOfBounds;

            foreach (ITerrain piece in existing)
            {
                if (Overlaps(candidate, piece.Shape, GapMarginInches))
                    return TerrainPlacementValidity.OverlapsExistingTerrain;
            }

            return TerrainPlacementValidity.Valid;
        }

        private static bool IsInsideTable(IZone zone, float tableW, float tableH)
        {
            foreach (var prim in zone.Primitives())
                if (!IsPrimitiveInsideTable(prim, tableW, tableH)) return false;
            return true;
        }

        private static bool IsPrimitiveInsideTable(IZone prim, float tableW, float tableH) => prim switch
        {
            RectangularZone r => r.Left >= 0f && r.Right <= tableW
                                 && r.Bottom >= 0f && r.Top <= tableH,
            CircularZone c => c.Center.X - c.Radius >= 0f
                              && c.Center.X + c.Radius <= tableW
                              && c.Center.Y - c.Radius >= 0f
                              && c.Center.Y + c.Radius <= tableH,
            _ => throw new NotSupportedException($"Unsupported primitive zone for bounds check: {prim.GetType().Name}.")
        };

        /// <summary>
        /// True if the two zones overlap or are within <paramref name="margin"/> of each
        /// other. Composites are decomposed to primitives and the cartesian product is
        /// tested pairwise.
        /// </summary>
        private static bool Overlaps(IZone a, IZone b, float margin)
        {
            foreach (var pa in a.Primitives())
                foreach (var pb in b.Primitives())
                    if (PrimitiveOverlaps(pa, pb, margin)) return true;
            return false;
        }

        private static bool PrimitiveOverlaps(IZone a, IZone b, float margin) => (a, b) switch
        {
            (RectangularZone ra, RectangularZone rb) => RectRectOverlap(ra, rb, margin),
            (CircularZone ca, CircularZone cb) => CircleCircleOverlap(ca, cb, margin),
            (RectangularZone r, CircularZone c) => RectCircleOverlap(r, c, margin),
            (CircularZone c, RectangularZone r) => RectCircleOverlap(r, c, margin),
            _ => throw new NotSupportedException($"Unsupported primitive zone pair for overlap: {a.GetType().Name} vs {b.GetType().Name}.")
        };

        private static bool RectRectOverlap(RectangularZone a, RectangularZone b, float margin)
        {
            if (a.Right + margin <= b.Left) return false;
            if (b.Right + margin <= a.Left) return false;
            if (a.Top + margin <= b.Bottom) return false;
            if (b.Top + margin <= a.Bottom) return false;
            return true;
        }

        private static bool CircleCircleOverlap(CircularZone a, CircularZone b, float margin)
        {
            Float2 d = a.Center - b.Center;
            float distSq = d.X * d.X + d.Y * d.Y;
            float threshold = a.Radius + b.Radius + margin;
            return distSq < threshold * threshold;
        }

        private static bool RectCircleOverlap(RectangularZone r, CircularZone c, float margin)
        {
            float closestX = Math.Clamp(c.Center.X, r.Left, r.Right);
            float closestY = Math.Clamp(c.Center.Y, r.Bottom, r.Top);
            float dx = c.Center.X - closestX;
            float dy = c.Center.Y - closestY;
            float distSq = dx * dx + dy * dy;
            float threshold = c.Radius + margin;
            return distSq < threshold * threshold;
        }
    }
}
