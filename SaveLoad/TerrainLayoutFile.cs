using System;
using System.Collections.Generic;

namespace FDG.SaveLoad
{
    [Serializable]
    public class TerrainLayoutFile
    {
        public const string EXTENSION_NO_PERIOD = "fdgterrain";
        public const string EXTENSION_WITH_PERIOD = "." + EXTENSION_NO_PERIOD;

        public string Name { get; set; } = string.Empty;

        public List<TerrainPieceEntry> Pieces { get; set; } = new List<TerrainPieceEntry>();
    }

    [Serializable]
    public class TerrainPieceEntry
    {
        /// <summary>
        /// #268 — optional display name ("Boulder", "Shipping container"). Resolvers fall back to the
        /// type + dimensions label when it is empty, so layouts written before this field, and
        /// hand-authored ones that omit it, are unaffected. Purely cosmetic: nothing keys off it.
        /// ASCII only, like every other user-facing string (see CLAUDE.md).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        public ETerrainType TerrainType { get; set; }

        //Polymorphic — IZone is serialized with TypeNameHandling.Auto so RectangularZone / CircularZone round-trip.
        public IZone Shape { get; set; } = null!;

        public float HeightInches { get; set; }

        /// <summary>
        /// #299 - what the piece costs in "Alternating: Points" terrain placement. 1-3 across the
        /// built-in palette today (roughly footprint x how strongly the type shapes play), but not
        /// hard-capped - a future monster piece may cost more. Defaults to 1 so layouts written
        /// before this field price every piece at the minimum; ignored by every other placement mode.
        /// </summary>
        public int Points { get; set; } = 1;
    }
}
