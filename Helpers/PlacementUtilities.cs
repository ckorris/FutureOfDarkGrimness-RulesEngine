namespace FDG
{
    /// <summary>
    /// Shared deployment-placement checks used by the AI / CLI / GUI place resolvers so a model's base
    /// is never set down inside impassible terrain (#048). Mirrors the base-radius treatment movement
    /// validation uses (#050): a model occupies a disc of its base radius, so the test is disc-vs-zone,
    /// not point-vs-zone.
    /// </summary>
    public static class PlacementUtilities
    {
        /// <summary>
        /// How close (inches) a base's edge must be to a zone edge to count as "touching" it — float/click
        /// slack on the #029 "comes back on touching a table edge" placement rule.
        /// </summary>
        public const float TABLE_EDGE_TOUCH_TOLERANCE_INCHES = 0.5f;

        /// <summary>
        /// The default facing for units placed in a deployment zone: toward the table centre, so they start
        /// aimed at the enemy — a zone above the centre line faces −Z, at or below it +Z (#150). Rotation
        /// input then applies relative to this. TODO: side zones (deployment on the left/right flank) would
        /// face ±X — add the X-axis cases if a map layout ever places zones there.
        /// </summary>
        public static Float2 DefaultDeployFacing(ZoneBounds zoneBounds, float tableHeightInches) =>
            zoneBounds.CenterZ > tableHeightInches * 0.5f ? new Float2(0f, -1f) : new Float2(0f, 1f);

        /// <summary>
        /// True if a base centred at <paramref name="centre"/> touches an edge of <paramref name="bounds"/> —
        /// its circumscribing circle comes within the tolerance of the nearest edge. Rotation-safe (uses the
        /// circumscribed radius, so any facing counts). #029: the Aircraft off-table redeploy must come back
        /// on touching a table edge.
        /// </summary>
        public static bool TouchesZoneEdge(Position centre, float circumscribedRadiusInches, ZoneBounds bounds)
        {
            float nearestEdge = MathF.Min(
                MathF.Min(centre.x - bounds.Left, bounds.Right - centre.x),
                MathF.Min(centre.z - bounds.Bottom, bounds.Top - centre.z));
            return nearestEdge <= circumscribedRadiusInches + TABLE_EDGE_TOUCH_TOLERANCE_INCHES;
        }

        /// <summary>
        /// True if a model with base radius <paramref name="radius"/> placed at <paramref name="center"/>
        /// would overlap any <see cref="ETerrainType.Impassible"/> piece in <paramref name="terrain"/>.
        /// </summary>
        public static bool OverlapsImpassibleTerrain(Position center, float radius, IEnumerable<ITerrain>? terrain)
        {
            if (terrain == null) return false;

            //A zero-length path p->p inflated by the base radius is exactly the model's footprint disc,
            //so the swept-disc overload doubles as a disc-vs-zone overlap test.
            Float2 p = new Float2(center.x, center.z);
            foreach (ITerrain piece in terrain)
            {
                if (piece.TerrainType.HasFlag(ETerrainType.Impassible)
                    && piece.Shape.DoesPathIntersectZone(p, p, radius))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
