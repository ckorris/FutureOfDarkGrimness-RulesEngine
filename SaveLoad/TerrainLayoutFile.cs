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
        public ETerrainType TerrainType { get; set; }

        //Polymorphic — IZone is serialized with TypeNameHandling.Auto so RectangularZone / CircularZone round-trip.
        public IZone Shape { get; set; } = null!;

        public float HeightInches { get; set; }
    }
}
