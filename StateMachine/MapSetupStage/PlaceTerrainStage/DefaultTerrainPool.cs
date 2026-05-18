using FDG.SaveLoad;

namespace FDG.Stages
{
    /// <summary>
    /// Built-in terrain pool used by <see cref="ETerrainPlacementMode.AutoFromLayout"/>
    /// (placed verbatim) and as the template pool for
    /// <see cref="ETerrainPlacementMode.Alternating"/> (positions ignored; only
    /// dimensions + type matter). User-supplied layouts (<c>LoadFromFile</c>) bypass
    /// this and use the file's pieces directly.
    /// </summary>
    /// <remarks>
    /// Defined in code rather than as a JSON asset for v1 — externalizing to a
    /// shipped <see cref="TerrainLayoutFile"/> asset is tracked under #044
    /// (multi-pool selection).
    /// </remarks>
    public static class DefaultTerrainPool
    {
        public static TerrainLayoutFile Get() => new TerrainLayoutFile
        {
            Name = "Default Pool",
            Pieces = new List<TerrainPieceEntry>
            {
                // Center building — blocking + impassible.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new RectangularZone(33, 39, 22, 26),
                    HeightInches = 4f,
                },
                // Forest, left-center — cover + difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                    Shape = new CircularZone(20, 24, 5),
                    HeightInches = 0f,
                },
                // Forest, right-center — cover + difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                    Shape = new CircularZone(52, 24, 5),
                    HeightInches = 0f,
                },
                // Sandbags near each deployment line — cover.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(28, 36, 12, 13),
                    HeightInches = 0f,
                },
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(36, 44, 35, 36),
                    HeightInches = 0f,
                },
                // Mine field — dangerous.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Dangerous,
                    Shape = new RectangularZone(8, 14, 30, 36),
                    HeightInches = 0f,
                },
                // Rubble — difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Difficult,
                    Shape = new RectangularZone(58, 66, 12, 18),
                    HeightInches = 0f,
                },
            }
        };
    }
}
