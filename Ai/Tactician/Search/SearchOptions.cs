using FDG.Ai;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// The tree's knobs (#191 B2). Everything the design doc says is "tuned at B4 on the benchmark"
    /// is a field here, never a literal in the tree code.
    /// </summary>
    public sealed record SearchOptions
    {
        /// <summary>
        /// Progressive widening k(N) = ceil(C * N^alpha) (sec 4.1), applied at both levels.
        /// <para>
        /// C was 2.0 at B2 ("tuned at B4 on the benchmark"). B4 lowered it to 0.5 on the measurement
        /// the design doc asked for (2k, 20 iterations, one worker): C=2.0 opened 17 root edges and
        /// reached depth 2; C=1.0 opened 9 and reached depth 2; C=0.5 opened 4 and reached depth 5.
        /// At an actual benchmark budget C=2.0 reached max depth ONE - the search saw no reply at
        /// all, which is A's horizon with extra steps, and B exists precisely for the multi-ply
        /// consequences (focus fire, activation economy - the Titan Lords probe). Expansions cost
        /// ~44ms wall at 4 workers, so a budget buys tens of nodes, not thousands: they have to be
        /// spent on depth. Which value PLAYS best is a games question and belongs to the B-gate;
        /// this is the value that makes the search a search.
        /// </para>
        /// </summary>
        public float WideningC { get; init; } = 0.5f;

        public float WideningAlpha { get; init; } = 0.5f;

        /// <summary>The generator's ranking budget for level-2 enumeration (sec 3.2).</summary>
        public int CandidateBudget { get; init; } = MacroActionGenerator.DefaultCandidateBudget;

        /// <summary>
        /// Natural activations played after the edge before the child boundary (sec 4.2). 0 = the
        /// child is the very next boundary, whichever player it belongs to. Depth is a parameter.
        /// </summary>
        public int Continuation { get; init; } = 0;

        /// <summary>
        /// This worker's seed (sec 6): every edge's simulation seed is derived from it with the
        /// node depth and edge index, so a worker is reproducible and two workers never share a
        /// determinization.
        /// </summary>
        public int WorkerSeed { get; init; } = 0;

        /// <summary>The policy natural activations play under inside a simulation (a B4 decision).</summary>
        public EAiProfile InSimProfile { get; init; } = EAiProfile.Tactician;

        public ERandomnessType Randomness { get; init; } = ERandomnessType.Probabilistic;

        public int TimeoutSeconds { get; init; } = 60;

        /// <summary>
        /// The search's hard deadline (#191 R9), handed to every simulation this tree runs: once it
        /// fires, an in-flight line stops at its next activation boundary and the tree opens nothing
        /// further. A line cut short this way does NOT close its edge (the search ended, the edge
        /// did not fail). <see cref="UctSearch"/> sets it from the time budget; iteration budgets
        /// (tests, G5 reproducibility) leave it unset.
        /// </summary>
        public CancellationToken Cancellation { get; init; }

        /// <summary>Children a node (or a unit branch) may have after <paramref name="visits"/> visits; never below 1.</summary>
        public int AllowedChildren(int visits) =>
            Math.Max(1, (int)MathF.Ceiling(WideningC * MathF.Pow(Math.Max(visits, 0), WideningAlpha)));
    }
}
