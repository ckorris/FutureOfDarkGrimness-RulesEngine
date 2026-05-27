using FDG.SaveLoad;

namespace FDG.Stages
{
    /// <summary>
    /// Built-in terrain pool. Templates only — design-time positions are ignored;
    /// only the dimensions + type matter. Used by AutoFromLayout (verbatim) and by
    /// Alternating (as the pool the player picks from). Externalization to a JSON
    /// asset is tracked under #044.
    /// </summary>
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
                // Left-side ruined building — blocking + impassible.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new RectangularZone(8, 14, 18, 24),
                    HeightInches = 3f,
                },

                // Small building.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new RectangularZone(0, 4, 0, 3),
                    HeightInches = 3f,
                },
                // Tall building.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new RectangularZone(0, 5, 0, 5),
                    HeightInches = 5f,
                },
                // L-shaped building (small). 4x4 bounding box.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(0, 4, 0, 2),  // horizontal bar
                        new RectangularZone(0, 2, 2, 4),  // vertical bar above-left
                    }),
                    HeightInches = 4f,
                },
                // L-shaped building (large). 6x6 bounding box.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(0, 6, 0, 2),  // horizontal bar
                        new RectangularZone(0, 2, 2, 6),  // vertical bar above-left
                    }),
                    HeightInches = 4f,
                },
                // Forest — cover + difficult. Rectangular (brown patch).
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                    Shape = new RectangularZone(0, 6, 0, 6),
                    HeightInches = 0f,
                },
                // Sandbags — cover.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(0, 8, 0, 1),
                    HeightInches = 0f,
                },
                // Mine field — dangerous.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Dangerous,
                    Shape = new RectangularZone(0, 6, 0, 6),
                    HeightInches = 0f,
                },
                // Rubble — difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Difficult,
                    Shape = new RectangularZone(0, 8, 0, 6),
                    HeightInches = 0f,
                },
            }
        };
    }
}
