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
        // Type shorthands. #399: Impassible and Blocking always travel together in the built-in pool -
        // if you cannot walk into it, you cannot see through it either. The old "Impassible alone =
        // walk around it, shoot over it" tier is gone (Tank traps, Water pool and Rocky ridge were its
        // only members and are Solid now): in play it read as an invisible wall, since nothing on the
        // table distinguishes a piece that stops a model from one that also stops a sight line. There is
        // deliberately no coercion in TerrainData - a hand-authored layout file may still separate them,
        // and the sight-line and movement rules still read the two flags independently.
        private const ETerrainType Solid = ETerrainType.Blocking | ETerrainType.Impassible;
        private const ETerrainType Woods = ETerrainType.Cover | ETerrainType.Difficult;

        // #393 - the gap a #393 piece leaves for models to walk through. Two 28mm bases
        // (BaseShapeDefaults.CircleDiameterInches, 1.1023622") abreast is 2.205", so 2.5" clears them
        // with room for the float-precision margins the placement/movement validators carry. Anything
        // tighter reads as a doorway and plays as a wall.
        private const float DoorwayInches = 2.5f;

        // The Cathedral shell's side walls run between the south and north walls, y 1..7 - six inches for
        // two 2.5" doors. Putting both in the middle left a straight horizontal firing lane in one side and
        // out the other, so they sit at OPPOSITE ends of the run: west low (y 1 -> 3.5), east high
        // (y 4.5 -> 7). Their y ranges are disjoint by WallBetweenDoorsInches, so no horizontal sight line
        // finds both, and 6 - 2 x 2.5 = 1" is the most separation two doors this wide can have.
        private const float WallRunLow  = 1f;
        private const float WallRunHigh = 7f;
        private const float WallBetweenDoorsInches = 1f;
        private const float WestDoorTop    = WallRunLow + DoorwayInches;             // 3.5
        private const float EastDoorBottom = WestDoorTop + WallBetweenDoorsInches;   // 4.5, and + 2.5 = 7

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
                    Points = 3,
                },
                // Forest, left-center — cover + difficult.
                new TerrainPieceEntry
                {
                    Name = "Forest",
                    TerrainType = Woods,
                    Shape = new CircularZone(20, 24, 5),
                    Points = 3,
                },
                // Forest, right-center — cover + difficult.
                new TerrainPieceEntry
                {
                    Name = "Forest",
                    TerrainType = Woods,
                    Shape = new CircularZone(52, 24, 5),
                    Points = 3,
                },
                // Sandbags, near team-1 line — cover.
                new TerrainPieceEntry
                {
                    Name = "Sandbag line",
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(28, 36, 12, 13),
                    Points = 1,
                },
                // Sandbags, near team-2 line — cover.
                new TerrainPieceEntry
                {
                    Name = "Sandbag line",
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(36, 44, 35, 36),
                    Points = 1,
                },
                // Mine field — dangerous.
                new TerrainPieceEntry
                {
                    Name = "Mine field",
                    TerrainType = ETerrainType.Dangerous,
                    Shape = new RectangularZone(8, 14, 30, 36),
                    Points = 2,
                },
                // Rubble — difficult.
                new TerrainPieceEntry
                {
                    Name = "Rubble",
                    TerrainType = ETerrainType.Difficult,
                    Shape = new RectangularZone(58, 66, 12, 18),
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
        /// authored position (name, type, footprint AABB, cost). Height dropped with #399 - every
        /// built-in piece leaves it at 0, so it can no longer tell two templates apart.
        /// </summary>
        private static (string, ETerrainType, float, float, int) TemplateKey(TerrainPieceEntry p)
        {
            (float lx, float hx, float ly, float hy) = p.Shape.GetAABB();
            return (p.Name, p.TerrainType, MathF.Round(hx - lx, 2), MathF.Round(hy - ly, 2), p.Points);
        }

        /// <summary>
        /// #268 — palette-only templates, in two groups. The first is SMALL impassible objects, which the
        /// built-in set was short of: every impassible piece in the auto layout is a 6-11" compound, so
        /// there was nothing to break up a firing lane with. The second (#393) is the opposite end — LARGE
        /// 3-point pieces, because the heavy tier was only seven pieces and games were exhausting it.
        /// Ordered small-to-large within each group; the picker re-sorts by cost anyway.
        ///
        /// <para><see cref="ETerrainType.Elevated"/> is deliberately unused: the flag is declared but no
        /// engine code reads it today, so a piece carrying it would look meaningful and do nothing.</para>
        /// </summary>
        private static IEnumerable<TerrainPieceEntry> ExtraTemplates()
        {
            // --- Small solid obstacles: block movement AND sight ---

            yield return Piece("Standing stone", Solid, new CircularZone(0, 0, 0.75f), 1);
            yield return Piece("Boulder", Solid, new CircularZone(0, 0, 1.25f), 1);
            yield return Piece("Watchtower", Solid, new RectangularZone(0, 2, 0, 2), 1);
            yield return Piece("Wrecked vehicle", Solid, new RectangularZone(0, 3, 0, 1.5f), 1);
            yield return Piece("Shipping container", Solid, new RectangularZone(0, 4, 0, 2), 2);
            yield return Piece("Bunker", Solid, new RectangularZone(0, 3, 0, 3), 2);
            yield return Piece("Wall segment", Solid, new RectangularZone(0, 3, 0, 0.75f), 1);
            yield return Piece("Long wall", Solid, new RectangularZone(0, 6, 0, 0.75f), 2);

            // Corner wall — two thin arms, for tucking a unit behind.
            yield return Piece("Corner wall", Solid, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 4, 0, 0.75f),
                new RectangularZone(0, 0.75f, 0, 4),
            }), 1);

            // Rock cluster — three boulders, an irregular small blocker.
            yield return Piece("Rock cluster", Solid, new CompositeZone(new List<IZone>
            {
                new CircularZone(1.2f, 1.2f, 1.2f),
                new CircularZone(3.2f, 2.0f, 0.9f),
                new CircularZone(2.0f, 3.4f, 0.8f),
            }), 2);

            // --- Low, solid ground obstacles: small, and they close a lane rather than screen one ---

            yield return Piece("Tank traps", Solid, new RectangularZone(0, 5, 0, 1), 1);
            yield return Piece("Water pool", Solid, new CircularZone(0, 0, 2f), 1);

            // --- Other cover / movement terrain, for variety ---

            yield return Piece("Copse", Woods, new CircularZone(0, 0, 2.5f), 2);
            yield return Piece("Ruined building", Woods, new RectangularZone(0, 5, 0, 4), 2);
            yield return Piece("Sandbag corner", ETerrainType.Cover, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 5, 0, 0.75f),
                new RectangularZone(0, 0.75f, 0, 5),
            }), 1);
            yield return Piece("Crater", ETerrainType.Cover | ETerrainType.Difficult, new CircularZone(0, 0, 2f), 1);
            yield return Piece("Marsh", ETerrainType.Difficult, new CircularZone(0, 0, 3f), 2);
            yield return Piece("Stream", ETerrainType.Difficult, new RectangularZone(0, 8, 0, 1.5f), 1);
            yield return Piece("Barbed wire", ETerrainType.Dangerous | ETerrainType.Difficult,
                new RectangularZone(0, 5, 0, 1), 1);

            // --- #393: the heavy end. Every piece here is 3 points and sized to the biggest templates
            // already shipping (Collapsed wall 11", Forest 10" across), because the reported problem was
            // that the 3-point tier was small enough to exhaust: seven pieces, so every game ended up
            // placing one of each. Palette only - Get() is deliberately untouched, so AutoFromLayout
            // still produces the same map. Spread across all four filter types (#394) on purpose.

            // Hollow wall ring with a doorway in each side wall, DoorwayInches wide so two 28mm bases fit
            // through abreast - the interior is a real place to stand, a courtyard the 10" walls screen
            // from every direction but the two entrances. The doors are STAGGERED to opposite corners
            // (see the constants): centred, they would have made the piece a firing lane, since a shooter
            // could see in one door and out the other.
            yield return Piece("Cathedral shell", Solid, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 10, 0, 1),                 // south wall
                new RectangularZone(0, 10, 7, 8),                 // north wall
                // The side walls run PAST the end walls rather than abutting them. Two rectangles that
                // merely touch leave a zero-width seam, and a sight line laid exactly along it grazes both
                // boundaries and counts as hitting neither - a hole in the wall one ten-thousandth of an
                // inch tall. Overlapping the corners removes the seam instead of relying on luck.
                new RectangularZone(0, 1, WestDoorTop, 8),        // west wall - its door is below it
                new RectangularZone(9, 10, 0, EastDoorBottom),    // east wall - its door is above it
            }), 3);

            // Three staggered towers, and the tallest thing in the palette - nothing shoots over it. The
            // towers stand DoorwayInches apart, so the streets between them take two 28mm bases abreast
            // and the piece plays as a block to move through rather than one to walk around.
            yield return Piece("Hab block", Solid, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 4, 0, 4),            // south-west tower
                new RectangularZone(6.5f, 10.5f, 0, 3.5f),  // south-east tower, 2.5" street to the west
                new RectangularZone(1.5f, 6.5f, 6.5f, 10),  // north tower, 2.5" street to the south
            }), 3);

            // The auto layout's Forest at full size and irregular: cover a whole unit fits inside.
            yield return Piece("Ancient wood", Woods, new CompositeZone(new List<IZone>
            {
                new CircularZone(3.5f, 3.5f, 3.5f),
                new CircularZone(7.5f, 4.5f, 3f),
                new CircularZone(4f, 7.5f, 2.5f),
            }), 3);

            // Large hazard: the palette had none - Mine field (6x6) was the biggest Dangerous piece.
            yield return Piece("Sunken mire", ETerrainType.Difficult | ETerrainType.Dangerous,
                new CompositeZone(new List<IZone>
            {
                new CircularZone(3.5f, 3.5f, 3.5f),
                new CircularZone(7.5f, 4f, 3f),
                new CircularZone(5.5f, 7.5f, 2.5f),
            }), 3);

            // Dangerous only: costs nothing to shoot across, everything to walk across.
            yield return Piece("Mine belt", ETerrainType.Dangerous, new RectangularZone(0, 9, 0, 7), 3);

            // Dog-legged firing position - cover on two facings, long enough for a full firing line.
            yield return Piece("Trench line", ETerrainType.Cover, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 4.5f, 0, 1.25f),
                new RectangularZone(3.25f, 4.5f, 1.25f, 3.5f),
                new RectangularZone(4.5f, 10, 2.25f, 3.5f),
            }), 3);

            // Long and thin: laid across a lane it closes the whole lane.
            yield return Piece("Crashed hauler", Solid, new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 11, 1.5f, 4.5f),
                new RectangularZone(1.5f, 4.5f, 0, 1.5f),
                new RectangularZone(6.5f, 9.5f, 4.5f, 6),
            }), 3);

            // A long broken spine, the widest low blocker in the palette: laid across the middle it turns
            // one open flank into two, since nothing walks through it and nothing sees past it (#399).
            yield return Piece("Rocky ridge", Solid, new CompositeZone(new List<IZone>
            {
                // Centres alternate low/high by ~1.2" so the spine reads as a broken ridge rather than a
                // ruled line. Consecutive rocks still overlap (centre gap < sum of radii), so the barrier
                // has no hole a model could slip through.
                new CircularZone(1.5f, 1.5f, 1.5f),
                new CircularZone(4.2f, 2.7f, 1.8f),
                new CircularZone(6.9f, 1.7f, 1.5f),
                new CircularZone(9.3f, 2.9f, 1.7f),
            }), 3);

            // Widest footprint in the palette at 11.5" - two 6" cylinders joined by a gantry.
            yield return Piece("Refinery tanks", Solid, new CompositeZone(new List<IZone>
            {
                new CircularZone(3f, 3f, 3f),
                new CircularZone(8.5f, 3f, 3f),
                new RectangularZone(3f, 8.5f, 2.25f, 3.75f),
            }), 3);

            // Three bands with 2" lanes between them, so a model can stand clear of the wire between two
            // bands instead of being caught straddling them - crossing it is a decision, not an accident.
            yield return Piece("Razorwire belt", ETerrainType.Dangerous | ETerrainType.Difficult,
                new CompositeZone(new List<IZone>
            {
                new RectangularZone(0, 10, 0, 1),
                new RectangularZone(0, 10, 3, 4),
                new RectangularZone(0, 10, 6, 7),
            }), 3);
        }

        // #399: no heightInches parameter. TerrainPieceEntry still carries the field for hand-authored
        // layout and scenario files, but nothing in the rules reads a terrain piece's height (sight lines
        // are a flat 2D footprint query - see TerrainData.EvaluateSightLine), so an authored value was a
        // number the tooltip printed and the game ignored. Every built-in piece leaves it at 0.
        private static TerrainPieceEntry Piece(string name, ETerrainType type, IZone shape, int points) =>
            new TerrainPieceEntry
            {
                Name = name,
                TerrainType = type,
                Shape = shape,
                Points = points,
            };
    }
}
