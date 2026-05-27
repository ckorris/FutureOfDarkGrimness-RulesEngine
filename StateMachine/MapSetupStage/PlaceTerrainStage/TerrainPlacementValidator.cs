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
    /// placement. Footprint must be fully inside the table and must not overlap
    /// (touching counts as overlap; a small <see cref="GapMarginInches"/> guards
    /// against float drift). All shape pairs reduce to per-primitive checks via
    /// <see cref="ZoneExtensions.Primitives"/>, so composites (L-shapes) and
    /// rotated rectangles (OBBs) work transparently — overlap math for OBB pairs
    /// uses SAT (separating axis theorem).
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

        private static bool IsPrimitiveInsideTable(IZone prim, float tableW, float tableH)
        {
            // The rotated-rect AABB is conservative — all 4 corners inside the
            // table iff the AABB is inside, so AABB-based bounds work here.
            (float lx, float hx, float ly, float hy) = prim switch
            {
                RectangularZone r => (r.Left, r.Right, r.Bottom, r.Top),
                CircularZone c => (c.Center.X - c.Radius, c.Center.X + c.Radius,
                                   c.Center.Y - c.Radius, c.Center.Y + c.Radius),
                RotatedZoneWrapper _ => prim.GetAABB(),
                _ => throw new NotSupportedException($"Unsupported primitive zone for bounds check: {prim.GetType().Name}.")
            };
            return lx >= 0f && hx <= tableW && ly >= 0f && hy <= tableH;
        }

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

            (RotatedZoneWrapper wa, RotatedZoneWrapper wb)
                => OBBsOverlap(MakeOBB(wa), MakeOBB(wb), margin),
            (RotatedZoneWrapper w, RectangularZone r)
                => OBBsOverlap(MakeOBB(w), MakeOBB(r), margin),
            (RectangularZone r, RotatedZoneWrapper w)
                => OBBsOverlap(MakeOBB(r), MakeOBB(w), margin),
            (RotatedZoneWrapper w, CircularZone c)
                => OBBCircleOverlap(MakeOBB(w), c, margin),
            (CircularZone c, RotatedZoneWrapper w)
                => OBBCircleOverlap(MakeOBB(w), c, margin),

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

        // ── OBB math ────────────────────────────────────────────────────────────

        private readonly struct OBB
        {
            public readonly Float2 Center;
            public readonly float HalfW;
            public readonly float HalfH;
            public readonly float AngleRad;

            public OBB(Float2 center, float halfW, float halfH, float angleRad)
            {
                Center = center; HalfW = halfW; HalfH = halfH; AngleRad = angleRad;
            }
        }

        private static OBB MakeOBB(RectangularZone r) =>
            new OBB(
                new Float2((r.Left + r.Right) * 0.5f, (r.Bottom + r.Top) * 0.5f),
                (r.Right - r.Left) * 0.5f,
                (r.Top - r.Bottom) * 0.5f,
                0f);

        private static OBB MakeOBB(RotatedZoneWrapper w)
        {
            if (w.Inner is not RectangularZone r)
                throw new InvalidOperationException(
                    $"{nameof(RotatedZoneWrapper)} inner must be a {nameof(RectangularZone)} after Primitives() flattening; got {w.Inner.GetType().Name}.");
            Float2 innerCenter = new Float2((r.Left + r.Right) * 0.5f, (r.Bottom + r.Top) * 0.5f);
            Float2 rotatedCenter = ZoneExtensions.RotateAround(innerCenter, w.Pivot, w.AngleDegrees);
            return new OBB(rotatedCenter,
                (r.Right - r.Left) * 0.5f,
                (r.Top - r.Bottom) * 0.5f,
                w.AngleDegrees * MathF.PI / 180f);
        }

        private static bool OBBsOverlap(OBB a, OBB b, float margin)
        {
            float cosA = MathF.Cos(a.AngleRad), sinA = MathF.Sin(a.AngleRad);
            float cosB = MathF.Cos(b.AngleRad), sinB = MathF.Sin(b.AngleRad);
            Float2 ax0 = new Float2(cosA, sinA);
            Float2 ax1 = new Float2(-sinA, cosA);
            Float2 bx0 = new Float2(cosB, sinB);
            Float2 bx1 = new Float2(-sinB, cosB);

            Float2 d = new Float2(b.Center.X - a.Center.X, b.Center.Y - a.Center.Y);

            return !IsSeparatingAxis(ax0, d, a, ax0, ax1, b, bx0, bx1, margin)
                && !IsSeparatingAxis(ax1, d, a, ax0, ax1, b, bx0, bx1, margin)
                && !IsSeparatingAxis(bx0, d, a, ax0, ax1, b, bx0, bx1, margin)
                && !IsSeparatingAxis(bx1, d, a, ax0, ax1, b, bx0, bx1, margin);
        }

        private static bool IsSeparatingAxis(Float2 axis, Float2 d,
            OBB a, Float2 ax0, Float2 ax1, OBB b, Float2 bx0, Float2 bx1, float margin)
        {
            float ra = a.HalfW * MathF.Abs(Dot(ax0, axis)) + a.HalfH * MathF.Abs(Dot(ax1, axis));
            float rb = b.HalfW * MathF.Abs(Dot(bx0, axis)) + b.HalfH * MathF.Abs(Dot(bx1, axis));
            float dist = MathF.Abs(Dot(d, axis));
            return dist >= ra + rb + margin;
        }

        private static bool OBBCircleOverlap(OBB obb, CircularZone c, float margin)
        {
            float cos = MathF.Cos(obb.AngleRad), sin = MathF.Sin(obb.AngleRad);
            float dx = c.Center.X - obb.Center.X;
            float dy = c.Center.Y - obb.Center.Y;
            // Inverse rotation: transform circle center into OBB's local axis-aligned frame.
            float localX = dx * cos + dy * sin;
            float localY = -dx * sin + dy * cos;
            float closestX = Math.Clamp(localX, -obb.HalfW, obb.HalfW);
            float closestY = Math.Clamp(localY, -obb.HalfH, obb.HalfH);
            float ddx = localX - closestX;
            float ddy = localY - closestY;
            float distSq = ddx * ddx + ddy * ddy;
            float threshold = c.Radius + margin;
            return distSq < threshold * threshold;
        }

        private static float Dot(Float2 a, Float2 b) => a.X * b.X + a.Y * b.Y;
    }
}
