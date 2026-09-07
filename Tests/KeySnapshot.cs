using FDG.Ai.Tactician.Search;
using FDG.Data;

namespace FDG.Tests
{
    /// <summary>
    /// A search-node snapshot that is only a KEY (#396): the authored trees in SearchTreeTests and
    /// UctSearchTests never materialize a store, they look their nodes up by name. Materializing one
    /// is a test bug, and says so.
    /// </summary>
    internal sealed record KeySnapshot(string Key) : IStoreSnapshot
    {
        public GameDataStore Materialize() =>
            throw new InvalidOperationException($"KeySnapshot '{Key}' is a lookup key; an authored tree never materializes a store.");

        public string ToJson() => Key;
    }

    internal static class SnapshotKeys
    {
        public static string Key(SearchNode node) => ((KeySnapshot)node.Snapshot!).Key;
    }
}
