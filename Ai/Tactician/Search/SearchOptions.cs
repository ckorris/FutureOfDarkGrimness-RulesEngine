using FDG.Ai;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// The tree's knobs (#191 B2). Everything the design doc says is "tuned at B4 on the benchmark"
    /// is a field here, never a literal in the tree code.
    /// </summary>
    public sealed record SearchOptions
    {
        /// <summary>Progressive widening k(N) = ceil(C * N^alpha) (sec 4.1), applied at both levels.</summary>
        public float WideningC { get; init; } = 2f;

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

        /// <summary>Children a node (or a unit branch) may have after <paramref name="visits"/> visits; never below 1.</summary>
        public int AllowedChildren(int visits) =>
            Math.Max(1, (int)MathF.Ceiling(WideningC * MathF.Pow(Math.Max(visits, 0), WideningAlpha)));
    }
}
