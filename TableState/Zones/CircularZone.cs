
using Newtonsoft.Json;

namespace FDG
{
    public class CircularZone : IZone
    {
        public readonly Float2 Center;

        public readonly float Radius;

        [JsonConstructor]
        public CircularZone(Float2 center, float radius)
        {
            if (radius <= 0f)
            {
                throw new ArgumentException($"{nameof(CircularZone)} radius must be positive. Got: {radius}.");
            }

            Center = center;
            Radius = radius;
        }

        public CircularZone(float xPosition, float yPosition, float radius)
            : this(new Float2(xPosition, yPosition), radius) { }

        public bool IsPointWithinZone(Float2 position)
        {
            Float2 delta = position - Center;
            return delta.X * delta.X + delta.Y * delta.Y <= Radius * Radius;
        }

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
        {
            //Either endpoint inside? Trivially intersects.
            if (IsPointWithinZone(startPosition) || IsPointWithinZone(endPosition))
            {
                return true;
            }

            //Otherwise: distance from circle center to the segment <= radius?
            Float2 segment = endPosition - startPosition;
            Float2 fromStartToCenter = Center - startPosition;

            float segmentLengthSquared = segment.X * segment.X + segment.Y * segment.Y;
            if (segmentLengthSquared <= 0f)
            {
                //Degenerate segment; both endpoints already tested as outside above.
                return false;
            }

            float t = (fromStartToCenter.X * segment.X + fromStartToCenter.Y * segment.Y) / segmentLengthSquared;
            t = Math.Clamp(t, 0f, 1f);

            Float2 closestPointOnSegment = new Float2(
                startPosition.X + segment.X * t,
                startPosition.Y + segment.Y * t);

            Float2 closestDelta = closestPointOnSegment - Center;
            float distanceSquared = closestDelta.X * closestDelta.X + closestDelta.Y * closestDelta.Y;

            return distanceSquared <= Radius * Radius;
        }

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
        {
            //Inflating a circle by r just grows its radius; test the center against the segment.
            float effectiveRadius = Radius + Math.Max(0f, inflationRadius);
            float distanceSquared = SegmentGeometry.PointToSegmentDistanceSquared(Center, startPosition, endPosition);
            return distanceSquared <= effectiveRadius * effectiveRadius;
        }

        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
        {
            if (IsPointWithinZone(startPosition)) return startPosition;

            Float2 d = endPosition - startPosition;
            float a = d.X * d.X + d.Y * d.Y;
            if (a <= 0f) return null;

            Float2 f = startPosition - Center;
            float b = 2f * (f.X * d.X + f.Y * d.Y);
            float c = f.X * f.X + f.Y * f.Y - Radius * Radius;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return null;

            float sqrtDisc = MathF.Sqrt(disc);
            float t1 = (-b - sqrtDisc) / (2f * a);
            // t1 is the nearer intersection; t2 is the further one. Since start is
            // outside the circle, t1 is the entry point we want.
            if (t1 < 0f || t1 > 1f) return null;
            return new Float2(startPosition.X + d.X * t1, startPosition.Y + d.Y * t1);
        }
    }
}
