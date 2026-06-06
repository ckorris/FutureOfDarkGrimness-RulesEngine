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
        /// <summary>
        /// Returns the default terrain layout.
        /// <para>
        /// AutoFromLayout places these pieces verbatim at their design-time positions.
        /// Alternating mode uses them as placement templates — only shape/size matters,
        /// not the design-time position, because <see cref="TerrainTemplateUtilities.TranslateToCenter"/>
        /// repositions the shape to wherever the player clicks.
        /// </para>
        /// </summary>
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
                // Sandbags, near team-1 line — cover.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(28, 36, 12, 13),
                    HeightInches = 0f,
                },
                // Sandbags, near team-2 line — cover.
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
