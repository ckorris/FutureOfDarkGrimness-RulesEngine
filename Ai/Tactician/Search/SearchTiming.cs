using System.Diagnostics;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// #191 search perf pass diagnostic: wall-clock accumulators for the search's stages, opt-in via
    /// FDG_SEARCH_TIMING=1 (FdgLab b0 prints the report). Exists because the sampling profiler's
    /// fine-grained attribution proved unreliable - it oversamples allocation sites - and a change
    /// it predicted at ~30% measured at 0.7%. Off, every probe is one branch on a static readonly.
    /// </summary>
    public static class SearchTiming
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("FDG_SEARCH_TIMING") == "1";

        public enum Stage
        {
            Iteration, Select, EnumerateUnits, EnumerateEdges, ScratchMaterialize, ScratchPlanner,
            Candidates, Scoring, Expand, SimMaterialize, SimRegistries, SimServer, SimRun, Leaf,
        }

        private static readonly long[] Ticks = new long[Enum.GetValues<Stage>().Length];
        private static readonly long[] Counts = new long[Enum.GetValues<Stage>().Length];

        public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0L;

        public static void Stop(Stage stage, long start)
        {
            if (!Enabled) return;
            Interlocked.Add(ref Ticks[(int)stage], Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref Counts[(int)stage]);
        }

        public static void Reset()
        {
            Array.Clear(Ticks);
            Array.Clear(Counts);
        }

        public static string Report()
        {
            double toMs = 1000.0 / Stopwatch.Frequency;
            double iteration = Ticks[(int)Stage.Iteration] * toMs;
            var lines = new List<string> { "search timing (wall ms, inclusive; nested stages overlap their parents):" };
            foreach (Stage stage in Enum.GetValues<Stage>())
            {
                double ms = Ticks[(int)stage] * toMs;
                long n = Counts[(int)stage];
                lines.Add($"  {stage,-18} {ms,9:F0} ms  {n,7} calls  {(n == 0 ? 0 : ms / n),8:F2} ms/call  {(iteration <= 0 ? 0 : 100 * ms / iteration),5:F1}% of iteration");
            }
            return string.Join("\n", lines);
        }
    }
}
