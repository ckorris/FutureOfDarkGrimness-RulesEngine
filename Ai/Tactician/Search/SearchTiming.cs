using System.Collections.Concurrent;
using System.Diagnostics;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// #191 search perf pass diagnostic: wall-clock and allocation accumulators for the search's
    /// stages, opt-in via FDG_SEARCH_TIMING=1 (FdgLab b0 prints the report). Exists because the
    /// sampling profiler's fine-grained attribution proved unreliable - it oversamples allocation
    /// sites - and a change it predicted at ~30% measured at 0.7%. Off, every probe is one branch
    /// on a static readonly.
    ///
    /// Two views. The STAGE table is caller-level (search phases) plus callee-level drill-downs
    /// (rule dispatch, combat math, move queries, sight, pathfinding, move planning, objective
    /// projection); nested probes overlap their parents, so the table is inclusive and does not sum.
    /// The ENGINE-STAGE timeline attributes the time inside a simulation to the state-machine stage
    /// the simulated game was in: from entering a stage until the next stage is entered, or the
    /// simulation returns. Bytes are process-wide GC allocation (imprecise mode) - meaningful on a
    /// single-worker run only, like the timeline.
    /// </summary>
    public static class SearchTiming
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("FDG_SEARCH_TIMING") == "1";

        public enum Stage
        {
            Iteration, Select, EnumerateUnits, EnumerateEdges, ScratchMaterialize, ScratchPlanner,
            Candidates, Scoring, Expand, SimMaterialize, SimRegistries, SimServer, SimRun, SimCapture, Leaf,
            // Callee-level drill-down (overlap their callers, and each other where nested).
            RuleDispatch, Combat, MoveQuery, Sight, Pathfind, PlanMove, ObjectiveProj,
            // Inside one volley estimate (Combat's drill-down).
            VolleyHit, VolleySave, VolleyMods, VolleyComplete,
            // Inside a simulated activation's Choose Action stage.
            PlannerChoose, Enumerate, MeleeRange, Request,
            // Inside move planning (PlanMove).
            PlanRoute, PlanFootprints, PlanCandidate, PlanValidate,
            // A simulated activation's action gates (ChooseActionStage.Enter) and the simulated
            // server's construction (FDGServer resume ctor).
            GateMove, GateCharge, GateShoot, GatePass, GateCast, GateAllowed, GateOffers,
            SrvResolver, SrvRestore, SrvLaunch,
        }

        /// <summary>What <see cref="Start"/> hands back; pass it to <see cref="Stop"/>.</summary>
        public readonly record struct Probe(long Ticks, long Bytes);

        private static readonly int StageCount = Enum.GetValues<Stage>().Length;
        private static readonly long[] Ticks = new long[StageCount];
        private static readonly long[] Counts = new long[StageCount];
        private static readonly long[] Bytes = new long[StageCount];

        private sealed class Span
        {
            public long Ticks;
            public long Count;
            public long Bytes;
        }

        private static readonly ConcurrentDictionary<string, Span> Spans = new();
        private static readonly ConcurrentDictionary<string, long> Notes = new();

        /// <summary>Counts an event by key (why an edge closed, which prescription fell through).</summary>
        public static void Note(string key)
        {
            if (!Enabled) return;
            Notes.AddOrUpdate(key, 1, (_, n) => n + 1);
        }
        private static readonly object SpanLock = new();
        private static string? _openSpan;
        private static long _openTicks;
        private static long _openBytes;

        public static Probe Start() =>
            Enabled ? new Probe(Stopwatch.GetTimestamp(), GC.GetTotalAllocatedBytes(precise: false)) : default;

        public static void Stop(Stage stage, Probe start)
        {
            if (!Enabled) return;
            Interlocked.Add(ref Ticks[(int)stage], Stopwatch.GetTimestamp() - start.Ticks);
            Interlocked.Add(ref Bytes[(int)stage], GC.GetTotalAllocatedBytes(precise: false) - start.Bytes);
            Interlocked.Increment(ref Counts[(int)stage]);
        }

        /// <summary>
        /// Engine-stage timeline: the state machine reports every stage it enters (any layer). The
        /// span that was open closes and is credited to the stage that was running.
        /// </summary>
        public static void StageEntered(string name)
        {
            if (!Enabled) return;
            long now = Stopwatch.GetTimestamp();
            long bytes = GC.GetTotalAllocatedBytes(precise: false);
            lock (SpanLock)
            {
                CloseOpenSpanLocked(now, bytes);
                _openSpan = name;
                _openTicks = now;
                _openBytes = bytes;
            }
        }

        /// <summary>A simulation returned: whatever stage was running stops accruing.</summary>
        public static void CloseStageSpan()
        {
            if (!Enabled) return;
            long now = Stopwatch.GetTimestamp();
            long bytes = GC.GetTotalAllocatedBytes(precise: false);
            lock (SpanLock) CloseOpenSpanLocked(now, bytes);
        }

        private static void CloseOpenSpanLocked(long now, long bytes)
        {
            if (_openSpan == null) return;
            Span span = Spans.GetOrAdd(_openSpan, _ => new Span());
            span.Ticks += now - _openTicks;
            span.Bytes += bytes - _openBytes;
            span.Count++;
            _openSpan = null;
        }

        public static void Reset()
        {
            Array.Clear(Ticks);
            Array.Clear(Counts);
            Array.Clear(Bytes);
            lock (SpanLock)
            {
                Spans.Clear();
                _openSpan = null;
            }
            Notes.Clear();
        }

        public static string Report()
        {
            double toMs = 1000.0 / Stopwatch.Frequency;
            double iteration = Ticks[(int)Stage.Iteration] * toMs;
            var lines = new List<string>
            {
                "search timing (wall ms, inclusive; nested stages overlap their parents; KB = GC-allocated, process-wide):",
            };
            foreach (Stage stage in Enum.GetValues<Stage>())
            {
                double ms = Ticks[(int)stage] * toMs;
                long n = Counts[(int)stage];
                double kb = Bytes[(int)stage] / 1024.0;
                lines.Add($"  {stage,-18} {ms,9:F0} ms  {n,7} calls  {(n == 0 ? 0 : ms / n),8:F2} ms/call  " +
                          $"{(iteration <= 0 ? 0 : 100 * ms / iteration),5:F1}% of iteration  " +
                          $"{(n == 0 ? 0 : kb / n),8:F1} KB/call  {kb / 1024.0,7:F1} MB");
            }

            double simRun = Ticks[(int)Stage.SimRun] * toMs;
            lines.Add("engine stages inside simulations (entering a stage until the next stage is entered, or the simulation returns):");
            lock (SpanLock)
            {
                foreach ((string name, Span span) in Spans.OrderByDescending(kv => kv.Value.Ticks).Take(24))
                {
                    double ms = span.Ticks * toMs;
                    double kb = span.Bytes / 1024.0;
                    lines.Add($"  {name,-34} {ms,9:F0} ms  {span.Count,7} spans  {ms / Math.Max(1, span.Count),8:F2} ms/span  " +
                              $"{(simRun <= 0 ? 0 : 100 * ms / simRun),5:F1}% of SimRun  {kb / Math.Max(1, span.Count),8:F1} KB/span");
                }
            }
            if (!Notes.IsEmpty)
            {
                lines.Add("notes (event counts):");
                foreach ((string key, long n) in Notes.OrderByDescending(kv => kv.Value).Take(40))
                    lines.Add($"  {n,7}  {key}");
            }
            return string.Join("\n", lines);
        }
    }
}
