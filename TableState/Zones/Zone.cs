


namespace FDG
{
    public interface IZone
    {
        //TODO: Need to draw area somehow, and also know which surface it's on,
        //which is usually the table directly but can also be a raised platform, for instance.

        public bool IsPointWithinZone(Float2 position);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition);

        /// <summary>
        /// Swept-disc variant: whether a disc of radius <paramref name="inflationRadius"/> swept along
        /// the segment from <paramref name="startPosition"/> to <paramref name="endPosition"/> touches
        /// this zone. Equivalent to Minkowski-inflating the zone footprint by <paramref name="inflationRadius"/>
        /// — used so a moving model's *base* (not just its center) is tested against terrain. An
        /// <paramref name="inflationRadius"/> of 0 is the same as the zero-width overload.
        /// </summary>
        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius);

        /// <summary>
        /// Returns the point along the segment from <paramref name="startPosition"/> to
        /// <paramref name="endPosition"/> where the segment first enters this zone, or
        /// null if the segment never enters it. If <paramref name="startPosition"/> is
        /// already inside the zone, returns <paramref name="startPosition"/> unchanged.
        /// Used by <c>LineOfSightUtilities.GetFirstBlockingHit</c> to render a marker
        /// where a sight line is broken.
        /// </summary>
        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition);

        /// <summary>
        /// Returns the point along the segment from <paramref name="startPosition"/> to
        /// <paramref name="endPosition"/> where the segment LAST leaves this zone — the
        /// largest-t inside-to-outside boundary crossing — or null if the segment never
        /// intersects the zone, or if <paramref name="endPosition"/> is inside it (no
        /// final exit). The end-inside-means-null contract is load-bearing for the #201
        /// cover proximity rules: a target standing inside a cover piece has no exit
        /// point, so the attacker-exit rule must not apply.
        /// </summary>
        public Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition);
    }
}
