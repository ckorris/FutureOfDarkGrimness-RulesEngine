using FDG.SaveLoad;

namespace FDG.Stages
{
    /// <summary>
    /// Built-in terrain. Two distinct things live here (#268):
    ///
    /// <list type="bullet">
    ///   <item><see cref="Get"/> — the AUTO LAYOUT. AutoFromLayout places these pieces verbatim at their
    ///     design-time positions, so this list defines how a generated map actually looks. Adding to it
    ///     makes every auto map denser, which is why the palette below is separate.</item>
    ///   <item><see cref="GetPalette"/> — the TEMPLATE PALETTE the Alternating-mode picker offers. Only
    ///     shape/size/type matter here, not position: <see cref="TerrainTemplateUtilities.TranslateToCenter"/>
    ///     moves the shape to wherever the player clicks. It is a superset of the auto layout.</item>
    /// </list>
    ///
    /// Externalization to a JSON asset is tracked under #044; a lobby picker for which layout file feeds
    /// each mode is #049.
    /// </summary>
    public static class DefaultTerrainPool
    {
        // Type shorthands. Blocking|Impassible = blocks movement AND line of sight (a building you can
        // neither enter nor shoot through). Impassible alone = blocks movement only, so a unit must go
        // around it but can still shoot over it.
        private const ETerrainType Solid   = ETerrainType.Blocking | ETerrainType.Impassible;
        private const ETerrainType Blocker = ETerrainType.Impassible;
        private const ETerrainType Woods   = ETerrainType.Cover | ETerrainType.Difficult;

        /// <summary>
        /// The default auto layout, placed verbatim by AutoFromLayout. Unchanged by #268 on purpose:
        /// widening the player's choice of pieces should not silently change how generated maps play.
        /// </summary>
        public static TerrainLayoutFile Get() => new TerrainLayoutFile
        {
            Name = "Default Pool",
            Pieces = new List<TerrainPieceEntry>
            {
                // Center building — blocking + impassible.
                new TerrainPieceEntry
                {
                    Name = "Central building",
                    TerrainType = Solid,
                    Shape = new RectangularZone(33, 39, 22, 26),
                    HeightInches = 4f,
                    Points = 3,
                },
                // Forest, left-center — cover + difficult.
                new TerrainPieceEntry
                {
                    Name = "Forest",
                    TerrainType = Woods,
                    Shape = new CircularZone(20, 24, 5),
                    HeightInches = 0f,
                    Points = 3,
                },
                // Forest, right-center — cover + difficult.
                new TerrainPieceEntry
                {
                    Name = "Forest",
                    TerrainType = Woods,
                    Shape = new CircularZone(52, 24, 5),
                    HeightInches = 0f,
                    Points = 3,
                },
                // Sandbags, near team-1 line — cover.
                new TerrainPieceEntry
                {
                    Name = "Sandbag line",
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(28, 36, 12, 13),
                    HeightInches = 0f,
                    Points = 1,
                },
                // Sandbags, near team-2 line — cover.
                new TerrainPieceEntry
                {
                    Name = "Sandbag line",
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(36, 44, 35, 36),
                    HeightInches = 0f,
                    Points = 1,
                },
                // Mine field — dangerous.
                new TerrainPieceEntry
                {
                    Name = "Mine field",
                    TerrainType = ETerrainType.Dangerous,
                    Shape = new RectangularZone(8, 14, 30, 36),
                    HeightInches = 0f,
                    Points = 2,
                },
                // Rubble — difficult.
                new TerrainPieceEntry
                {
                    Name = "Rubble",
                    TerrainType = ETerrainType.Difficult,
                    Shape = new RectangularZone(58, 66, 12, 18),
                    HeightInches = 0f,
                    Points = 2,
                },

                // --- Compound impassible obstacles (small/medium, scattered in the open quadrants) ---
                // Totally impassible: Blocking | Impassible, so they block both movement AND line of sight
                // (like the center building) — you can't shoot over them. Hand-placed so they don't overlap
                // the pieces above; each is a CompositeZone of a few rectangles for an irregular shape.

                // Rocky outcrop, top-left — L-shape.
                new TerrainPieceEntry
                {
                    Name = "Rocky outcrop",
                    TerrainType = Solid,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(6, 13, 6, 8),    // horizontal arm
                        new RectangularZone(6, 8, 8, 13),    // vertical arm
                    }),
                    HeightInches = 3f,
                    Points = 3,
                },
                // Wreckage, top-center — T-shape.
                new TerrainPieceEntry
                {
                    Name = "Wreckage",
                    TerrainType = Solid,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(42, 50, 6, 8),   // cross bar
                        new RectangularZone(45, 47, 8, 13),  // stem
                    }),
                    HeightInches = 2.5f,
                    Points = 3,
                },
                // Crater rim, top-right — U-shape opening upward.
                new TerrainPieceEntry
                {
                    Name = "Crater rim",
                    TerrainType = Solid,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(58, 60, 5, 11),  // left arm
                        new RectangularZone(58, 67, 5, 7),   // base
                        new RectangularZone(65, 67, 5, 11),  // right arm
                    }),
                    HeightInches = 0f,
                    Points = 3,
                },
                // Tank traps, bottom-left — plus/cross.
                new TerrainPieceEntry
                {
                    Name = "Tank trap cluster",
                    TerrainType = Solid,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(8, 14, 40, 42),  // horizontal
                        new RectangularZone(10, 12, 38, 44), // vertical
                    }),
                    HeightInches = 2f,
                    Points = 3,
                },
                // Collapsed wall, bottom-right — stepped Z.
                new TerrainPieceEntry
                {
                    Name = "Collapsed wall",
                    TerrainType = Solid,
                    Shape = new CompositeZone(new List<IZone>
                    {
                        new RectangularZone(56, 61, 38, 40), // top step
                        new RectangularZone(59, 64, 40, 42), // mid step
                        new RectangularZone(62, 67, 42, 44), // bottom step
                    }),
                    HeightInches = 3f,
                    Points = 3,
                },
            }
        };

        /// <summary>
        /// The Alternating-mode picker's palette: the auto layout's pieces plus <see cref="ExtraTemplates"/>.
        /// The picker scales its thumbnails to the largest piece and scrolls, so the list length is free.
        ///
        /// <para>#301: sorted by point cost (cheap first; ties keep the curated order) and offered once
        /// per distinct TEMPLATE - the auto layout contributes two Forests and two Sandbag lines that
        /// differ only by their baked-in positions, which mean nothing for a template the player
        /// positions anyway.</para>
        /// </summary>
        public static IReadOnlyList<TerrainPieceEntry> GetPalette()
        {
            var palette = new List<TerrainPieceEntry>(Get().Pieces);
            palette.AddRange(ExtraTemplates());
            return palette
                .DistinctBy(TemplateKey)
                .OrderBy(p => p.Points)   // OrderBy is stable, so equal-cost pieces keep their order
                .ToList();
        }

        /// <summary>
        /// Identity of a template for de-duplication: everything a placed copy inherits except the
        /// authored position (name, type, footprint AABB, height, cost).
        /// </summary>
        private static (string, ETerrainType, float, float, float, int) TemplateKey(TerrainPieceEntry p)
        {
            (float lx, float hx, float ly, float hy) = p.Shape.GetAABB();
            return (p.Name, p.TerrainType, MathF.Round(hx - lx, 2), MathF.Round(hy - ly, 2), p.HeightInches, p.Points);
        }

        /// <summary>
        /// #268 — palette-only templates. The bulk are SMALL impassible objects, which the built-in set was
        /// short of: every impassible piece in the auto layout is a 6-11" compound, so there was nothing to
        /// break up a firing lane with. Ordered small-to-large within each group.
        ///
        /// <para><see cref="ETerrainType.Elevated"/> is deliberately unused: the flag is declared but no
        /// engine code reads it today, so a piece carrying it would look meaningful and do nothing.</para>
        /// </summary>
        private static IEnumerable<TerrainPieceEntry> ExtraTemplates()
        {
            // --- Small solid obstacles: block movement AND sight ---

            yield return Piece("Standing stone", Solid, new CircularZone(0, 0, 0.75f), 4f, 1);
            yield return Piece("Boulder", Solid, new CircularZone(0, 0, 1.25f), 2.5f, 1);
            yield return Piece("Watchtower", Solid, new RectangularZone(0, 2, 0, 2), 7f, 1);
            yield return Piece("Wrecked vehicle", Solid, new RectangularZone(0, 3, 0, 1.5f), 2f, 1);
            yield return Piece("Shipping container", Solid, new RectangularZone(0, 4, 0, 2), 2.5f, 2);
            yield return Piece("Bunker", Solid, new RectangularZone(0, 3, 0, 3), 3f, 2);
            yield return Piece("Wall segment", Solid, new RectangularZone(0, 3, 0, 0.75f), 2.5f, 1);
            yield return Piece("Long wall", Solid, new RectangularZone(0, 6, 0, 0.75f), 2.5f, 2);

            // Corner wall — two thin arms, for tucking a unit behind.
            yield return Piece("Corner wall", Solid, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 4, 0, 0.75f),
                new RectangularZone(0, 0.75f, 0, 4),
            }), 2.5f, 1);

            // Rock cluster — three boulders, an irregular small blocker.
            yield return Piece("Rock cluster", Solid, new CompositeZone(new List<IZone>
            {
                new CircularZone(1.2f, 1.2f, 1.2f),
                new CircularZone(3.2f, 2.0f, 0.9f),
                new CircularZone(2.0f, 3.4f, 0.8f),
            }), 2.5f, 2);

            // --- Impassible but NOT blocking: go around it, shoot over it ---

            yield return Piece("Tank traps", Blocker, new RectangularZone(0, 5, 0, 1), 0.5f, 1);
            yield return Piece("Water pool", Blocker, new CircularZone(0, 0, 2f), 0f, 1);

            // --- Other cover / movement terrain, for variety ---

            yield return Piece("Copse", Woods, new CircularZone(0, 0, 2.5f), 0f, 2);
            yield return Piece("Ruined building", Woods, new RectangularZone(0, 5, 0, 4), 0f, 2);
            yield return Piece("Sandbag corner", ETerrainType.Cover, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 5, 0, 0.75f),
                new RectangularZone(0, 0.75f, 0, 5),
            }), 0f, 1);
            yield return Piece("Crater", ETerrainType.Cover | ETerrainType.Difficult, new CircularZone(0, 0, 2f), 0f, 1);
            yield return Piece("Marsh", ETerrainType.Difficult, new CircularZone(0, 0, 3f), 0f, 2);
            yield return Piece("Stream", ETerrainType.Difficult, new RectangularZone(0, 8, 0, 1.5f), 0f, 1);
            yield return Piece("Barbed wire", ETerrainType.Dangerous | ETerrainType.Difficult,
                new RectangularZone(0, 5, 0, 1), 0f, 1);
        }

        private static TerrainPieceEntry Piece(string name, ETerrainType type, IZone shape, float heightInches, int points) =>
            new TerrainPieceEntry
            {
                Name = name,
                TerrainType = type,
                Shape = shape,
                HeightInches = heightInches,
                Points = points,
            };
    }
}
