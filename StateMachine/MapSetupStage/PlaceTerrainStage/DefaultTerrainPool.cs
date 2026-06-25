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

                // --- Compound impassible obstacles (small/medium, scattered in the open quadrants) ---
                // Totally impassible: Blocking | Impassible, so they block both movement AND line of sight
                // (like the center building) — you can't shoot over them. Hand-placed so they don't overlap
                // the pieces above; each is a CompositeZone of a few rectangles for an irregular shape.

                // Rocky outcrop, top-left — L-shape.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(6, 13, 6, 8),    // horizontal arm
                        new RectangularZone(6, 8, 8, 13),    // vertical arm
                    }),
                    HeightInches = 3f,
                },
                // Wreckage, top-center — T-shape.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(42, 50, 6, 8),   // cross bar
                        new RectangularZone(45, 47, 8, 13),  // stem
                    }),
                    HeightInches = 2.5f,
                },
                // Crater rim, top-right — U-shape opening upward.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(58, 60, 5, 11),  // left arm
                        new RectangularZone(58, 67, 5, 7),   // base
                        new RectangularZone(65, 67, 5, 11),  // right arm
                    }),
                    HeightInches = 0f,
                },
                // Tank traps, bottom-left — plus/cross.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(8, 14, 40, 42),  // horizontal
                        new RectangularZone(10, 12, 38, 44), // vertical
                    }),
                    HeightInches = 2f,
                },
                // Collapsed wall, bottom-right — stepped Z.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(56, 61, 38, 40), // top step
                        new RectangularZone(59, 64, 40, 42), // mid step
                        new RectangularZone(62, 67, 42, 44), // bottom step
                    }),
                    HeightInches = 3f,
                },
            }
        };
    }
}
