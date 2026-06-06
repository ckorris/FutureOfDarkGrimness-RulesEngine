


namespace FDG
{
    public interface IZone
    {
        //TODO: Need to draw area somehow, and also know which surface it's on,
        //which is usually the table directly but can also be a raised platform, for instance.

        public bool IsPointWithinZone(Float2 position);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition);

        /// <summary>
        /// Returns the point along the segment from <paramref name="startPosition"/> to
        /// <paramref name="endPosition"/> where the segment first enters this zone, or
        /// null if the segment never enters it. If <paramref name="startPosition"/> is
        /// already inside the zone, returns <paramref name="startPosition"/> unchanged.
        /// Used by <c>LineOfSightUtilities.GetFirstBlockingHit</c> to render a marker
        /// where a sight line is broken.
        /// </summary>
        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition);
    }
}
