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
