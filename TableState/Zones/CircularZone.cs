

namespace FDG
{
    public class CircularZone : IZone
    {
        public readonly Float2 Center;

        public readonly float Radius;

        public CircularZone(Float2 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public CircularZone(float xPosition, float yPosition, float radius)
        {
            Center = new Float2(xPosition, yPosition);
            Radius = radius;
        }

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
        {
            throw new NotImplementedException();
        }

        public bool IsPointWithinZone(Float2 position)
        {
            throw new NotImplementedException();
        }
    }
}
