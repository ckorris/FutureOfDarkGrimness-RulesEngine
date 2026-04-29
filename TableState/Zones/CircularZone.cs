
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
    }
}
