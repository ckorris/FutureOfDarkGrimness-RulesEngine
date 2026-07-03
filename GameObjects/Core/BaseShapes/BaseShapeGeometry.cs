using System;

namespace FDG
{
    /// <summary>
    /// Exact base-to-base distance and collision between two <see cref="IBaseShape"/>s (#149/#150). This is
    /// THE shape-aware collision seam: every model-to-model measurement (spacing, melee/charge reach,
    /// coherency, deploy/movement overlap) routes here. Each base yields a <see cref="BaseFootprint"/> — a
    /// rounded convex hull — and a single hull-vs-hull routine measures any pair at any facing. There is no
    /// per-shape-pair switch: a new shape only implements <see cref="IBaseShape.Footprint"/> and collides
    /// against every existing shape for free. Circle-vs-circle stays byte-identical to <c>dist − rA − rB</c>
    /// (two single-point hulls), preserving the engine's determinism discipline. Retires the GJK seam #153.
    /// </summary>
    public static class BaseShapeGeometry
    {
        /// <summary>
        /// Horizontal (X/Z) gap between the surfaces of base <paramref name="a"/> at <paramref name="posA"/>
        /// facing <paramref name="facingA"/> and base <paramref name="b"/> at <paramref name="posB"/> facing
        /// <paramref name="facingB"/> (yaw unit normals along local +Z; zero → forward). Zero when the bases
        /// just touch, negative when they overlap (a penetration estimate), positive when apart. Vertical (Y)
        /// distance is ignored — combine via <see cref="DistanceUtilities.GetBaseToBaseDistanceInches_3D"/>.
        /// </summary>
        public static float SurfaceGap2D(IBaseShape a, Position posA, Float2 facingA,
            IBaseShape b, Position posB, Float2 facingB)
        {
            BaseFootprint fa = a.Footprint(posA, facingA);
            BaseFootprint fb = b.Footprint(posB, facingB);
            // Gap between the rounded bodies = gap between their hull cores, less both inflation radii.
            return ConvexHullGap(fa.Corners, fb.Corners) - fa.Rounding - fb.Rounding;
        }

        /// <summary>
        /// Facing-less overload: measures both bases as forward (+Z). Circles are rotation-invariant, so this
        /// is exact for them; for a rectangle it assumes the axis-aligned (width→X, height→Z) default. Kept
        /// for callers with no orientation to supply.
        /// </summary>
        public static float SurfaceGap2D(IBaseShape a, Position posA, IBaseShape b, Position posB) =>
            SurfaceGap2D(a, posA, new Float2(0f, 1f), b, posB, new Float2(0f, 1f));

        /// <summary>
        /// True when two bases overlap (their surface gap is negative). The one-call "are these colliding?"
        /// test — facing-aware, shape-aware, no per-shape branching at the call site.
        /// </summary>
        public static bool AreColliding(IBaseShape a, Position posA, Float2 facingA,
            IBaseShape b, Position posB, Float2 facingB) =>
            SurfaceGap2D(a, posA, facingA, b, posB, facingB) < 0f;

        /// <summary>
        /// The farthest a base may translate from <paramref name="start"/> toward <paramref name="end"/>
        /// (facing held at <paramref name="movingFacing"/>) before its footprint first touches the static base
        /// <paramref name="otherShape"/> at <paramref name="otherPos"/>/<paramref name="otherFacing"/> (#155).
        /// The move-preview sibling of <see cref="AreColliding"/>: it answers "how far can I slide before I hit
        /// this model?" so a resolver can clamp a ghost to stop just short of an enemy base instead of drawing a
        /// move that passes through it. Returns the full segment length when the slide never makes contact, and 0
        /// when the bases already touch at <paramref name="start"/>. The gap along a straight slide between two
        /// convex bodies is convex (one minimum), so a coarse forward scan brackets the first contact without
        /// skipping a graze, and a bisection refines it to <paramref name="toleranceInches"/>. The returned
        /// distance is a known-clear bound (the low side of the bracket).
        /// </summary>
        public static float MaxTravelBeforeBaseCollision(IBaseShape movingShape, Float2 start, Float2 end,
            Float2 movingFacing, IBaseShape otherShape, Position otherPos, Float2 otherFacing,
            float toleranceInches = 0.005f)
        {
            float GapAt(float t)
            {
                Position at = new Position(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
                return SurfaceGap2D(movingShape, at, movingFacing, otherShape, otherPos, otherFacing);
            }

            if (GapAt(0f) < 0f) return 0f;

            float dx = end.X - start.X, dz = end.Y - start.Y;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (length <= 0f) return 0f;

            // Coarse scan for the first sample that is in contact; step is fine enough (<= 0.2") that the gap
            // can't dip below zero and back above it between two samples for a real base.
            int steps = Math.Max(8, (int)MathF.Ceiling(length / 0.2f));
            float prevT = 0f;
            bool bracketed = false;
            float hiT = length;
            for (int i = 1; i <= steps; i++)
            {
                float t = length * i / steps;
                float paramT = t / length;
                if (GapAt(paramT) < 0f) { hiT = t; bracketed = true; break; }
                prevT = t;
            }
            if (!bracketed) return length; // never makes contact along the slide

            float tolerance = MathF.Max(toleranceInches, 1e-4f);
            float lo = prevT, hi = hiT; // lo: known clear; hi: known in contact
            while (hi - lo > tolerance)
            {
                float mid = (lo + hi) * 0.5f;
                if (GapAt(mid / length) < 0f) hi = mid;
                else lo = mid;
            }
            return lo;
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

            // Rotate the world offset into the base's local frame (local +Z = facing, local +X ⟂ facing) and let
            // the shape answer for itself — no shape switch, so a new IBaseShape just implements
            // DistanceToLocalPoint. A zero facing means forward (+Z), which is the identity transform.
            Float2 f = facing.X == 0f && facing.Y == 0f ? new Float2(0f, 1f) : facing;
            float localZ = dx * f.X + dz * f.Y;   // along facing (height axis)
            float localX = dx * f.Y - dz * f.X;   // along the perpendicular (width axis)
            return shape.DistanceToLocalPoint(localX, localZ);
        }

        /// <summary>
        /// AABB half-extents of <paramref name="shape"/>'s footprint along the table X and Z axes at
        /// <paramref name="facing"/> — how far the base reaches from its centre on each axis. Grid packers use
        /// this to space columns and rows PER AXIS: a single radius over-spaces the short axis of an elongated
        /// rectangle (breaking the 1" cohesion rule) or under-spaces the long axis (overlapping). Circle → (r, r);
        /// an axis-aligned W×H rectangle → (W/2, H/2).
        /// </summary>
        public static (float halfX, float halfZ) FootprintHalfExtents(IBaseShape shape, Float2 facing)
        {
            BaseFootprint fp = shape.Footprint(new Position(0f, 0f), facing);
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            foreach (Float2 c in fp.Corners)
            {
                if (c.X < minX) minX = c.X;
                if (c.X > maxX) maxX = c.X;
                if (c.Y < minZ) minZ = c.Y;
                if (c.Y > maxZ) maxZ = c.Y;
            }
            return ((maxX - minX) * 0.5f + fp.Rounding, (maxZ - minZ) * 0.5f + fp.Rounding);
        }

        // --- Rounded-convex-hull core -------------------------------------------------------------------
        // Signed distance between two convex hulls (each ≥ 1 corner): positive = nearest-feature gap when
        // disjoint, negative = penetration depth when they interpenetrate. Two single-point hulls never
        // interpenetrate, so circle-vs-circle falls to the separation branch and returns the exact centre
        // distance — the rounding subtraction in SurfaceGap2D then reproduces dist − rA − rB byte-for-byte.

        private static float ConvexHullGap(Float2[] a, Float2[] b)
        {
            if (HullsOverlap(a, b, out float penetration))
                return -penetration;
            return HullSeparation(a, b);
        }

        // Nearest-feature distance between two disjoint convex hulls: the min over every vertex-vs-edge and
        // vertex-vs-vertex pair. A point hull contributes no edges, so its terms reduce to vertex-vertex.
        private static float HullSeparation(Float2[] a, Float2[] b)
        {
            float best = float.PositiveInfinity;

            foreach (Float2 pa in a)
                foreach (Float2 pb in b)
                    best = MathF.Min(best, Distance(pa, pb));

            if (b.Length >= 2)
                foreach (Float2 pa in a)
                    for (int i = 0; i < b.Length; i++)
                        best = MathF.Min(best, PointSegDistance(pa, b[i], b[(i + 1) % b.Length]));

            if (a.Length >= 2)
                foreach (Float2 pb in b)
                    for (int i = 0; i < a.Length; i++)
                        best = MathF.Min(best, PointSegDistance(pb, a[i], a[(i + 1) % a.Length]));

            return best;
        }

        // Separating Axis Theorem: two convex hulls overlap iff no edge normal of either separates them. A
        // point hull has no edge normals; point-vs-point (no candidate axes) is treated as disjoint, which is
        // what keeps circle-vs-circle on the exact separation path above. Axes are unit length, so the least
        // slab overlap is a real penetration depth.
        private static bool HullsOverlap(Float2[] a, Float2[] b, out float penetration)
        {
            penetration = float.PositiveInfinity;
            bool anyAxis = false;

            if (SeparatedOnAnyEdgeNormal(a, a, b, ref penetration, ref anyAxis) ||
                SeparatedOnAnyEdgeNormal(b, a, b, ref penetration, ref anyAxis))
            {
                penetration = 0f;
                return false;
            }

            if (!anyAxis) { penetration = 0f; return false; } // point vs point — no interpenetration
            return true;
        }

        // Projects both hulls onto each unit edge normal of edgeSource. Returns true as soon as one axis
        // shows a gap (a separating axis); otherwise tracks the minimum overlap as a penetration estimate.
        private static bool SeparatedOnAnyEdgeNormal(Float2[] edgeSource, Float2[] a, Float2[] b,
            ref float penetration, ref bool anyAxis)
        {
            int n = edgeSource.Length;
            if (n < 2) return false; // a point contributes no edges

            for (int i = 0; i < n; i++)
            {
                Float2 v0 = edgeSource[i], v1 = edgeSource[(i + 1) % n];
                float ex = v1.X - v0.X, ez = v1.Y - v0.Y;
                float len = MathF.Sqrt(ex * ex + ez * ez);
                if (len == 0f) continue;

                Float2 axis = new Float2(-ez / len, ex / len); // unit outward edge normal
                anyAxis = true;

                (float aMin, float aMax) = Project(a, axis);
                (float bMin, float bMax) = Project(b, axis);
                float overlap = MathF.Min(aMax, bMax) - MathF.Max(aMin, bMin);
                if (overlap <= 0f) return true; // separated on this axis
                penetration = MathF.Min(penetration, overlap);
            }
            return false;
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

        private static float Distance(Float2 a, Float2 b)
        {
            float dx = b.X - a.X, dz = b.Y - a.Y;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static float PointSegDistance(Float2 p, Float2 a, Float2 b)
        {
            float ex = b.X - a.X, ez = b.Y - a.Y;
            float lenSq = ex * ex + ez * ez;
            if (lenSq == 0f) return Distance(p, a);

            float t = ((p.X - a.X) * ex + (p.Y - a.Y) * ez) / lenSq;
            t = MathF.Max(0f, MathF.Min(1f, t));
            return Distance(p, new Float2(a.X + t * ex, a.Y + t * ez));
        }
    }
}
