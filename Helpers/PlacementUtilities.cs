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
        /// True if a base of circumscribing radius <paramref name="circumscribedRadiusInches"/> centred at
        /// <paramref name="centre"/> lies entirely within <paramref name="zone"/> — its footprint fits inside the
        /// zone's bounding box (inset by the radius) AND its centre is inside the zone's true shape (so a circular
        /// zone constrains to the real circle, not its bounding square). Deployment zones sit flush against the
        /// table edge, so this is also what keeps a deployed base from poking off the table (#150 follow-up). Use
        /// the CIRCUMSCRIBING radius so the guarantee holds for a rectangular base at any facing.
        /// </summary>
        public static bool IsBaseWithinZone(Position centre, float circumscribedRadiusInches, IBoundedZone zone)
        {
            ZoneBounds b = zone.Bounds;
            return centre.x >= b.Left + circumscribedRadiusInches
                && centre.x <= b.Right - circumscribedRadiusInches
                && centre.z >= b.Bottom + circumscribedRadiusInches
                && centre.z <= b.Top - circumscribedRadiusInches
                && zone.IsPointWithinZone(centre);
        }

        /// <summary>
        /// Facing-aware overload (#150): insets each zone edge by the base's TRUE per-axis footprint extent at
        /// <paramref name="facing"/> — its real oriented bounding box — instead of the circumscribing circle.
        /// Exact (the whole base still stays inside the zone / on the table) without the circle's over-block: a
        /// rotated rectangle can sit as close to an edge as its actual reach in that direction, so it isn't
        /// stopped short on the axis it's thin against.
        /// </summary>
        public static bool IsBaseWithinZone(Position centre, IBaseShape shape, Float2 facing, IBoundedZone zone)
        {
            (float halfX, float halfZ) = BaseShapeGeometry.FootprintHalfExtents(shape, facing);
            ZoneBounds b = zone.Bounds;
            return centre.x >= b.Left + halfX
                && centre.x <= b.Right - halfX
                && centre.z >= b.Bottom + halfZ
                && centre.z <= b.Top - halfZ
                && zone.IsPointWithinZone(centre);
        }

        /// <summary>
        /// Clamps <paramref name="centre"/> so a base of circumscribing radius
        /// <paramref name="circumscribedRadiusInches"/> stays entirely on the table (a last-resort net for
        /// auto-placement when a unit is too big for its zone to hold cleanly — a base is never left off the
        /// board). The <see cref="MathF.Max"/> guards keep the range valid for a base wider than the table.
        /// </summary>
        public static Position ClampBaseWithinTable(Position centre, float circumscribedRadiusInches,
            float tableWidthInches, float tableHeightInches)
        {
            float r = circumscribedRadiusInches;
            float hiX = MathF.Max(r, tableWidthInches - r);
            float hiZ = MathF.Max(r, tableHeightInches - r);
            float x = MathF.Min(MathF.Max(centre.x, r), hiX);
            float z = MathF.Min(MathF.Max(centre.z, r), hiZ);
            return new Position(x, z);
        }

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

        /// <summary>
        /// Shape- and facing-aware overload (#150): true if <paramref name="shape"/> placed at
        /// <paramref name="center"/> facing <paramref name="facing"/> would overlap any impassible piece,
        /// measured by its true oriented footprint (a rotated rectangular base isn't over- or under-blocked
        /// like a circle would be). A zero-length sweep is the static footprint; a circular base reduces to
        /// the radius overload above.
        /// </summary>
        public static bool OverlapsImpassibleTerrain(Position center, IBaseShape shape, Float2 facing, IEnumerable<ITerrain>? terrain)
        {
            if (terrain == null) return false;

            Float2 p = new Float2(center.x, center.z);
            foreach (ITerrain piece in terrain)
            {
                if (piece.TerrainType.HasFlag(ETerrainType.Impassible)
                    && SweptBaseGeometry.DoesSweptBaseIntersectZone(piece.Shape, p, p, shape, facing))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
