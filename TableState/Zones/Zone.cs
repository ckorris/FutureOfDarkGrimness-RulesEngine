

using System.Diagnostics;

namespace FDG
{
    public interface IZone
    {
        //TODO: Need to draw area somehow, and also know which surface it's on,
        //which is usually the table directly but can also be a raised platform, for instance.

        public bool IsPointWithinZone(Position position);

        public bool DoesPathIntersectZone(Position startPosition, Position endPosition);
    }
}
