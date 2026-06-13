using System;

namespace FDG
{
    /// <summary>
    /// Shared 2D segment geometry used by the swept-disc (<c>inflationRadius</c>) overloads of
    /// <see cref="IZone.DoesPathIntersectZone(Float2, Float2, float)"/>. All distances are squared
    /// to avoid square roots at the call site (compare against <c>radius * radius</c>).
    /// </summary>
    internal static class SegmentGeometry
    {
        /// <summary>Squared shortest distance from point <paramref name="p"/> to segment a→b.</summary>
        public static float PointToSegmentDistanceSquared(Float2 p, Float2 a, Float2 b)
        {
            Float2 ab = b - a;
            float lengthSq = ab.X * ab.X + ab.Y * ab.Y;
            float t = lengthSq <= 0f ? 0f : ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSq;
            t = Math.Clamp(t, 0f, 1f);

            float closestX = a.X + ab.X * t;
            float closestY = a.Y + ab.Y * t;
            float dx = p.X - closestX;
            float dy = p.Y - closestY;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Squared shortest distance between segment a→b and segment c→d. Returns 0 if they intersect.
        /// </summary>
        public static float SegmentToSegmentDistanceSquared(Float2 a, Float2 b, Float2 c, Float2 d)
        {
            if (SegmentsIntersect(a, b, c, d)) return 0f;

            float best = PointToSegmentDistanceSquared(a, c, d);
            best = Math.Min(best, PointToSegmentDistanceSquared(b, c, d));
            best = Math.Min(best, PointToSegmentDistanceSquared(c, a, b));
            best = Math.Min(best, PointToSegmentDistanceSquared(d, a, b));
            return best;
        }

        /// <summary>Whether segments a→b and c→d intersect (including collinear/touching cases).</summary>
        public static bool SegmentsIntersect(Float2 a, Float2 b, Float2 c, Float2 d)
        {
            float d1 = Cross(d - c, a - c);
            float d2 = Cross(d - c, b - c);
            float d3 = Cross(b - a, c - a);
            float d4 = Cross(b - a, d - a);

            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            //Collinear / endpoint-touching cases.
            if (d1 == 0f && IsOnSegment(c, d, a)) return true;
            if (d2 == 0f && IsOnSegment(c, d, b)) return true;
            if (d3 == 0f && IsOnSegment(a, b, c)) return true;
            if (d4 == 0f && IsOnSegment(a, b, d)) return true;

            return false;
        }

        //Whether point p (known collinear with a→b) lies within the segment's bounding box.
        private static bool IsOnSegment(Float2 a, Float2 b, Float2 p)
        {
            return Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) &&
                   Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);
        }

        private static float Cross(Float2 v1, Float2 v2) => v1.X * v2.Y - v1.Y * v2.X;
    }
}
