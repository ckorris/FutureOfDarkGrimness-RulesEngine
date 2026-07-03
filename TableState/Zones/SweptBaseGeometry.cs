using System;

namespace FDG
{
    /// <summary>
    /// Shape-aware "does a model's base, swept from <c>start</c> to <c>end</c>, intersect a zone?" (#150).
    /// The shape-aware sibling of <see cref="IZone.DoesPathIntersectZone(Float2, Float2, float)"/>, which inflates
    /// the zone by a scalar radius (a swept <i>disc</i>) — exact only for a circular base. A rectangular base is
    /// tested against its true, facing-oriented footprint.
    ///
    /// A circular base reuses the existing scalar swept-disc path unchanged (exact, all zone types). A rectangular
    /// base is swept along the segment (translation only — bases don't rotate mid-move) into the convex hull of its
    /// corners at <c>start</c> and <c>end</c>, then tested by the Separating Axis Theorem against each zone primitive
    /// (<see cref="ZoneExtensions.Primitives"/> flattens any zone to axis-aligned rectangles, rotated rectangles, and
    /// circles). SAT projects the eight swept corners, so the convex hull never has to be built explicitly.
    /// </summary>
    public static class SweptBaseGeometry
    {
        public static bool DoesSweptBaseIntersectZone(IZone zone, Float2 start, Float2 end, IBaseShape baseShape, Float2 facing)
        {
            // A circular base is exactly the existing scalar swept-disc — reuse it (handles every zone type).
            if (baseShape is CircleBase circle)
                return zone.DoesPathIntersectZone(start, end, circle.RadiusInches);

            // Any non-rectangle shape (future): conservative bounding-circle, i.e. the pre-#150 behaviour.
            if (baseShape is not RectangleBase rect)
                return zone.DoesPathIntersectZone(start, end, baseShape.BoundingRadiusInches);

            Float2 f = facing.X == 0f && facing.Y == 0f ? new Float2(0f, 1f) : facing;

            foreach (IZone prim in zone.Primitives())
            {
                if (SweptRectIntersectsPrimitive(prim, start, end, rect, f))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The farthest a base may travel from <c>start</c> toward <c>end</c> before its swept footprint first
        /// touches <paramref name="zone"/> (#155) — the distance a move-preview clamp can allow along the segment
        /// without entering the zone. Returns the full segment length when the sweep never intersects, and 0 when
        /// the base's static footprint at <c>start</c> already touches. Found by bisection over
        /// <see cref="DoesSweptBaseIntersectZone"/> (the swept region only grows with travel, so the predicate is
        /// monotone), which keeps this exactly consistent with the boolean the validators use, for every
        /// base-shape/zone combination. The result is a known-clear distance (the bisection's low bound), accurate
        /// to <paramref name="toleranceInches"/>.
        /// </summary>
        public static float MaxTravelBeforeZoneIntersection(IZone zone, Float2 start, Float2 end,
            IBaseShape baseShape, Float2 facing, float toleranceInches = 0.005f)
        {
            if (DoesSweptBaseIntersectZone(zone, start, start, baseShape, facing)) return 0f;

            float dx = end.X - start.X, dy = end.Y - start.Y;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            if (length <= 0f) return 0f;
            if (!DoesSweptBaseIntersectZone(zone, start, end, baseShape, facing)) return length;

            float tolerance = MathF.Max(toleranceInches, 1e-4f);
            float invLength = 1f / length;
            float lo = 0f, hi = length; // lo: known clear; hi: known intersecting
            while (hi - lo > tolerance)
            {
                float mid = (lo + hi) * 0.5f;
                Float2 at = new Float2(start.X + dx * mid * invLength, start.Y + dy * mid * invLength);
                if (DoesSweptBaseIntersectZone(zone, start, at, baseShape, facing)) hi = mid;
                else lo = mid;
            }
            return lo;
        }

        private static bool SweptRectIntersectsPrimitive(IZone prim, Float2 start, Float2 end, RectangleBase rect, Float2 facing)
        {
            switch (prim)
            {
                case RectangularZone r:
                    return SweptRectVsAxisAlignedRect(start, end, rect, facing, r);

                // OBB: transform the swept base into the rectangle's local frame, where the zone is axis-aligned.
                case RotatedZoneWrapper w when w.Inner is RectangularZone r:
                    Float2 ls = ZoneExtensions.RotateAround(start, w.Pivot, -w.AngleDegrees);
                    Float2 le = ZoneExtensions.RotateAround(end, w.Pivot, -w.AngleDegrees);
                    Float2 lf = RotateDirection(facing, -w.AngleDegrees);
                    return SweptRectVsAxisAlignedRect(ls, le, rect, lf, r);

                case CircularZone c:
                    return SweptRectVsCircle(start, end, rect, facing, c.Center, c.Radius);

                default:
                    // Primitives() shouldn't yield anything else; degrade to the conservative bounding circle.
                    return prim.DoesPathIntersectZone(start, end, rect.BoundingRadiusInches);
            }
        }

        // The eight world corners of the base at start and at end (sweep = translation). The base's local +Z
        // (height) axis runs along facing, +X (width) perpendicular.
        private static Float2[] SweptRectCorners(Float2 start, Float2 end, RectangleBase rect, Float2 facing)
        {
            Float2 hAxis = facing;                            // local +Z (height)
            Float2 wAxis = new Float2(facing.Y, -facing.X);   // local +X (width), perpendicular
            float hw = rect.WidthInches * 0.5f, hh = rect.HeightInches * 0.5f;

            Float2[] pts = new Float2[8];
            int i = 0;
            Float2[] centres = { start, end };
            foreach (Float2 c in centres)
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        pts[i++] = new Float2(
                            c.X + sx * hw * wAxis.X + sz * hh * hAxis.X,
                            c.Y + sx * hw * wAxis.Y + sz * hh * hAxis.Y);
            return pts;
        }

        private static bool SweptRectVsAxisAlignedRect(Float2 start, Float2 end, RectangleBase rect, Float2 facing, RectangularZone r)
        {
            Float2[] baseCorners = SweptRectCorners(start, end, rect, facing);
            Float2[] zoneCorners =
            {
                new Float2(r.Left, r.Bottom), new Float2(r.Right, r.Bottom),
                new Float2(r.Right, r.Top),   new Float2(r.Left, r.Top),
            };

            // Candidate separating axes: the swept hull's edge normals (the base's two axes + the sweep
            // perpendicular) and the zone's two axes. Axes need not be unit length for separation testing.
            Float2 sweep = new Float2(end.X - start.X, end.Y - start.Y);
            Float2[] axes =
            {
                facing,
                new Float2(facing.Y, -facing.X),
                new Float2(-sweep.Y, sweep.X),  // perpendicular to the sweep (zero on a static query — skipped)
                new Float2(1f, 0f),
                new Float2(0f, 1f),
            };

            foreach (Float2 axis in axes)
            {
                if (axis.X == 0f && axis.Y == 0f) continue;
                if (Separated(baseCorners, zoneCorners, axis)) return false;
            }
            return true;
        }

        private static bool SweptRectVsCircle(Float2 start, Float2 end, RectangleBase rect, Float2 facing, Float2 centre, float radius)
        {
            Float2[] baseCorners = SweptRectCorners(start, end, rect, facing);

            // Poly-vs-circle SAT: the polygon's edge normals plus the axis from the centre to the nearest corner.
            Float2 nearest = baseCorners[0];
            float bestSq = float.PositiveInfinity;
            foreach (Float2 p in baseCorners)
            {
                float ndx = p.X - centre.X, ndy = p.Y - centre.Y;
                float sq = ndx * ndx + ndy * ndy;
                if (sq < bestSq) { bestSq = sq; nearest = p; }
            }

            Float2 sweep = new Float2(end.X - start.X, end.Y - start.Y);
            Float2[] axes =
            {
                facing,
                new Float2(facing.Y, -facing.X),
                new Float2(-sweep.Y, sweep.X),
                new Float2(nearest.X - centre.X, nearest.Y - centre.Y),
            };

            foreach (Float2 axis in axes)
            {
                if (axis.X == 0f && axis.Y == 0f) continue;
                (float bMin, float bMax) = Project(baseCorners, axis);
                float len = MathF.Sqrt(axis.X * axis.X + axis.Y * axis.Y);
                float c = centre.X * axis.X + centre.Y * axis.Y;
                float r = radius * len; // axis isn't unit length, so scale the circle's projected radius to match
                if (bMax < c - r || c + r < bMin) return false; // separated on this axis
            }
            return true;
        }

        private static bool Separated(Float2[] a, Float2[] b, Float2 axis)
        {
            (float aMin, float aMax) = Project(a, axis);
            (float bMin, float bMax) = Project(b, axis);
            return aMax < bMin || bMax < aMin;
        }

        private static (float min, float max) Project(Float2[] pts, Float2 axis)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (Float2 p in pts)
            {
                float d = p.X * axis.X + p.Y * axis.Y;
                if (d < min) min = d;
                if (d > max) max = d;
            }
            return (min, max);
        }

        // Rotates a direction vector (no pivot) by the given angle.
        private static Float2 RotateDirection(Float2 dir, float angleDegrees)
        {
            if (angleDegrees == 0f) return dir;
            float rad = angleDegrees * MathF.PI / 180f;
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            return new Float2(dir.X * cos - dir.Y * sin, dir.X * sin + dir.Y * cos);
        }
    }
}
