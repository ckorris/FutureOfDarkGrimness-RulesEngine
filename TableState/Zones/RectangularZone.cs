
namespace FDG
{
    /// <summary>
    /// Represents a 2D rectangular area on the map.
    /// </summary>
    public class RectangularZone : IZone
    {
        public readonly float Left;
        public readonly float Right;
        public readonly float Bottom;
        public readonly float Top;

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
            // Check for intersection with each edge
            return LinesIntersect(startPosition, endPosition, new Float2(Left, Bottom), new Float2(Left, Top)) ||
                   LinesIntersect(startPosition, endPosition, new Float2(Left, Top), new Float2(Right, Top)) ||
                   LinesIntersect(startPosition, endPosition, new Float2(Right, Top), new Float2(Right, Bottom)) ||
                   LinesIntersect(startPosition, endPosition, new Float2(Right, Bottom), new Float2(Left, Bottom)) ||
                   IsPointWithinZone(startPosition) || IsPointWithinZone(endPosition);
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


    }
}
