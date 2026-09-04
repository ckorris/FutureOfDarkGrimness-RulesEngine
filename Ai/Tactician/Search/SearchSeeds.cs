namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// Deterministic seed derivation for an edge's simulation (#191 B2 sec 6): a fixed integer mix
    /// of (worker seed, depth, unit index, edge index). Not <c>HashCode.Combine</c> - that is
    /// randomized per process, and a search must reproduce under a fixed seed (G5).
    /// </summary>
    public static class SearchSeeds
    {
        public static int Derive(int workerSeed, int depth, int unitIndex, int edgeIndex)
        {
            ulong x = unchecked((ulong)(uint)workerSeed * 0x9E3779B97F4A7C15UL);
            x = Mix(x ^ (ulong)(uint)depth);
            x = Mix(x ^ ((ulong)(uint)unitIndex << 16));
            x = Mix(x ^ ((ulong)(uint)edgeIndex << 32));
            // Keep it positive: the engine's seeds are ints and a negative one is fine but ugly in a log.
            return (int)(x & 0x7FFFFFFF);
        }

        // splitmix64's finalizer.
        private static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
