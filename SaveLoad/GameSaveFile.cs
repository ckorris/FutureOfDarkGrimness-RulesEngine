using FDG.Data.Containers;

namespace FDG.SaveLoad
{
    /// <summary>
    /// On-disk shape of a saved game: a version stamp, the store's type-map fingerprint (type full
    /// names + capacities, in TypeID order) so the store can be rebuilt identically and stale saves
    /// rejected, and the full store contents as reference/JSON pairs (the same payload the network
    /// layer uses for a full-state sync).
    /// </summary>
    public class GameSaveFile
    {
        public int Version { get; set; }

        public List<SavedTypeEntry> TypeMap { get; set; } = new List<SavedTypeEntry>();

        public List<ReferenceJsonValuePair> Entries { get; set; } = new List<ReferenceJsonValuePair>();
    }

    /// <summary>One registered store type: its <see cref="Type.FullName"/> and component-store capacity.</summary>
    public class SavedTypeEntry
    {
        public string FullName { get; set; } = "";

        public int Capacity { get; set; }

        public SavedTypeEntry() { }

        public SavedTypeEntry(string fullName, int capacity)
        {
            FullName = fullName;
            Capacity = capacity;
        }
    }
}
