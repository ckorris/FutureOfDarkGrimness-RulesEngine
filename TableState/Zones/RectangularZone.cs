
namespace FDG
{
    /// <summary>
    /// Represents a 2D rectangular area on the map.
    /// </summary>
    public class RectangularZone : IBoundedZone
    {
        public readonly float Left;
        public readonly float Right;
        public readonly float Bottom;
        public readonly float Top;

        public ZoneBounds Bounds => new ZoneBounds(Left, Right, Bottom, Top);

        public RectangularZone(float left, float right, float bottom, float top)
        {
            if(left >= right || bottom >= top)
            {
                throw new ArgumentException($"Tried to specify invalid zone. Left: {left} Right: {right} Bottom: {bottom} Top: {top}");
            }

            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
        }

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
        {
            if (CannotTouch(startPosition, endPosition, 0f)) return false;

            // Check for intersection with each edge
            return LinesIntersect(startPosition, endPosition, new Float2(Left, Bottom), new Float2(Left, Top)) ||
                   LinesIntersect(startPosition, endPosition, new Float2(Left, Top), new Float2(Right, Top)) ||
                   LinesIntersect(startPosition, endPosition, new Float2(Right, Top), new Float2(Right, Bottom)) ||
                   LinesIntersect(startPosition, endPosition, new Float2(Right, Bottom), new Float2(Left, Bottom)) ||
                   IsPointWithinZone(startPosition) || IsPointWithinZone(endPosition);
        }

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
        {
            if (inflationRadius <= 0f) return DoesPathIntersectZone(startPosition, endPosition);

            if (CannotTouch(startPosition, endPosition, inflationRadius)) return false;

            //A bare crossing (edge intersection or endpoint inside) hits regardless of radius.
            if (DoesPathIntersectZone(startPosition, endPosition)) return true;

            //Otherwise the swept disc hits iff the segment comes within inflationRadius of the boundary.
            Float2 bottomLeft = new Float2(Left, Bottom);
            Float2 topLeft = new Float2(Left, Top);
            Float2 topRight = new Float2(Right, Top);
            Float2 bottomRight = new Float2(Right, Bottom);

            float radiusSquared = inflationRadius * inflationRadius;
            return SegmentGeometry.SegmentToSegmentDistanceSquared(startPosition, endPosition, bottomLeft, topLeft) <= radiusSquared
                || SegmentGeometry.SegmentToSegmentDistanceSquared(startPosition, endPosition, topLeft, topRight) <= radiusSquared
                || SegmentGeometry.SegmentToSegmentDistanceSquared(startPosition, endPosition, topRight, bottomRight) <= radiusSquared
                || SegmentGeometry.SegmentToSegmentDistanceSquared(startPosition, endPosition, bottomRight, bottomLeft) <= radiusSquared;
        }

        /// <summary>
        /// Cheap axis-aligned rejection before any cross-product or segment-distance math (#191
        /// campaign step 3 profiling). A sight line is evaluated against EVERY terrain piece on the
        /// table - LineOfSightUtilities.EvaluateSightLine walks the whole list - and the Tactician
        /// runs one such test per (movement candidate x enemy), so the common case by far is a piece
        /// nowhere near the line. Four float comparisons replace 16 cross products (and, when
        /// inflated, four segment-to-segment distance computations) for that common miss.
        /// <para>
        /// Conservative by construction, so results are unchanged: it returns true only when the
        /// segment's bounding box, expanded by <paramref name="inflationRadius"/>, cannot overlap
        /// this zone's - and a segment whose expanded bounds miss the zone entirely can neither
        /// cross an edge nor come within the radius of one.
        /// </para>
        /// </summary>
        private bool CannotTouch(Float2 start, Float2 end, float inflationRadius)
        {
            float minX = start.X < end.X ? start.X : end.X;
            float maxX = start.X < end.X ? end.X : start.X;
            if (maxX < Left - inflationRadius || minX > Right + inflationRadius) return true;

            float minY = start.Y < end.Y ? start.Y : end.Y;
            float maxY = start.Y < end.Y ? end.Y : start.Y;
            return maxY < Bottom - inflationRadius || minY > Top + inflationRadius;
        }

        public bool IsPointWithinZone(Float2 position)
        {
            return position.X >= Left && position.X <= Right &&
                   position.Y >= Bottom && position.Y <= Top;
        }

        private static bool LinesIntersect(Float2 a, Float2 b, Float2 c, Float2 d)
        {
            // Check if two line segments intersect using cross product signs.
            float cross1 = Cross(c - a, b - a);
            float cross2 = Cross(d - a, b - a);
            float cross3 = Cross(a - c, d - c);
            float cross4 = Cross(b - c, d - c);

            return (cross1 * cross2 < 0 && cross3 * cross4 < 0);
        }

        private static float Cross(Float2 v1, Float2 v2)
        {
            return v1.X * v2.Y - v1.Y * v2.X;
        }

        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
        {
            if (IsPointWithinZone(startPosition)) return startPosition;

            Float2[] corners = {
                new Float2(Left, Bottom),
                new Float2(Left, Top),
                new Float2(Right, Top),
                new Float2(Right, Bottom),
            };

            float bestT = float.MaxValue;
            Float2 bestPoint = default;
            bool found = false;

            for (int i = 0; i < 4; i++)
            {
                if (TrySegmentIntersection(startPosition, endPosition, corners[i], corners[(i + 1) % 4],
                        out float t, out Float2 point))
                {
                    if (t < bestT) { bestT = t; bestPoint = point; found = true; }
                }
            }

            return found ? bestPoint : (Float2?)null;
        }

        public Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition)
        {
            if (IsPointWithinZone(endPosition)) return null;

            Float2[] corners = {
                new Float2(Left, Bottom),
                new Float2(Left, Top),
                new Float2(Right, Top),
                new Float2(Right, Bottom),
            };

            // The end is outside, so the largest-t boundary crossing is where the
            // segment last leaves the rectangle (whether the start is inside or the
            // segment passes clean through).
            float bestT = float.MinValue;
            Float2 bestPoint = default;
            bool found = false;

            for (int i = 0; i < 4; i++)
            {
                if (TrySegmentIntersection(startPosition, endPosition, corners[i], corners[(i + 1) % 4],
                        out float t, out Float2 point))
                {
                    if (t > bestT) { bestT = t; bestPoint = point; found = true; }
                }
            }

            return found ? bestPoint : (Float2?)null;
        }

        private static bool TrySegmentIntersection(Float2 p, Float2 p2, Float2 q, Float2 q2,
            out float t, out Float2 point)
        {
            Float2 r = p2 - p;
            Float2 s = q2 - q;
            float denom = Cross(r, s);
            if (denom == 0f) { t = 0f; point = default; return false; }

            Float2 qp = q - p;
            t = Cross(qp, s) / denom;
            float u = Cross(qp, r) / denom;
            if (t < 0f || t > 1f || u < 0f || u > 1f) { point = default; return false; }

            point = new Float2(p.X + r.X * t, p.Y + r.Y * t);
            return true;
        }
    }
}
