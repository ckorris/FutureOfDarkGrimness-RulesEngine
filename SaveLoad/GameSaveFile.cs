using FDG.Data.Containers;
using FDG.Replay;
using Newtonsoft.Json;

namespace FDG.SaveLoad
{
    /// <summary>
    /// On-disk shape of a saved game: a version stamp, the store's type-map fingerprint (a stable type ID
    /// per <see cref="SaveTypeRegistry"/> + capacity, in TypeID order) so the store can be rebuilt
    /// identically and stale saves rejected, and the full store contents as reference/JSON pairs (the same
    /// payload the network layer uses for a full-state sync).
    /// <para>#453: a save of a recorded game also carries the replay so far (<see cref="Replay"/>), so a resume
    /// continues one recording. Both replay fields are optional - absent from snapshots, scenarios and older saves,
    /// which load as before - and omitted from the file when null, so a save without a replay is byte-for-byte what
    /// it was before #453.</para>
    /// </summary>
    public class GameSaveFile
    {
        /// <summary>File extension for saved games (no leading period), mirroring `fdgarmy`/`fdglayout`.</summary>
        public const string EXTENSION_NO_PERIOD = "fdgsave";

        public const string EXTENSION_WITH_PERIOD = "." + EXTENSION_NO_PERIOD;

        public int Version { get; set; }

        public List<SavedTypeEntry> TypeMap { get; set; } = new List<SavedTypeEntry>();

        /// <summary>#453: what <see cref="Replay"/> holds, for a save browser that should not decompress it. Written
        /// ahead of the entries so a reader can stop early.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ReplaySummary? ReplaySummary { get; set; }

        public List<ReferenceJsonValuePair> Entries { get; set; } = new List<ReferenceJsonValuePair>();

        /// <summary>#453: the game's <c>.fdgreplay</c> as of the save (base64 of the same gzip bytes), ending with its
        /// <c>Saved</c> marker; null when the game was not being recorded.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Replay { get; set; }
    }

    /// <summary>
    /// One registered store type: its stable ID (<see cref="SaveTypeRegistry"/>) and component-store
    /// capacity. <see cref="TypeId"/> holds a stable ID for registered types and a <see cref="Type.FullName"/>
    /// as a fallback for anything unregistered (also how a hypothetical pre-#070 save's FullName is
    /// tolerated on load).
    /// </summary>
    public class SavedTypeEntry
    {
        public string TypeId { get; set; } = "";

        public int Capacity { get; set; }

        public SavedTypeEntry() { }

        public SavedTypeEntry(string typeId, int capacity)
        {
            TypeId = typeId;
            Capacity = capacity;
        }
    }
}
