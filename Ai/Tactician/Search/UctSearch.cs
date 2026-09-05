using System.Diagnostics;
using FDG.Simulation;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// B4 (#191 campaign step 8): time-budgeted UCT over the B2 tree, with root parallelism.
    /// Replaces <see cref="ExpansionScaffold"/> (B2's test-only walk) and touches nothing in the
    /// tree - selection reads exactly <see cref="SearchEdge.QFor"/> and <see cref="SearchEdge.Prior"/>,
    /// which is all the design doc (sec 7.4) lets B4 know about an edge.
    /// <para>
    /// <b>Root parallelism is load-bearing for correctness, not just speed</b> (design sec 6): every
    /// edge's simulation samples dice under a derived seed, so a subtree conditions on ONE
    /// determinization. Each worker is an independent tree with its own seed, and the root's answer
    /// sums visits across workers - so the choice is an ensemble over determinizations rather than a
    /// bet on one lucky sample. Workers share no mutable state.
    /// </para>
    /// <para>
    /// <b>Determinism (G5).</b> With <see cref="UctOptions.Iterations"/> set, a fixed
    /// <see cref="UctOptions.Workers"/> and a fixed <see cref="UctOptions.RootSeed"/>, the result is
    /// EXACT regardless of thread scheduling: each worker's tree is independent, each worker's
    /// descent is sequential, and the merge is a deterministic reduction in worker order. Under a
    /// TIME budget the iteration count varies with the box, so the result is not reproducible -
    /// that is what the iteration budget exists for, and every test uses it.
    /// </para>
    /// No transposition table (plan sec 9: states too large; revisit only with evidence).
    /// </summary>
    public static class UctSearch
    {
        /// <summary>
        /// One descent from the root: expand where progressive widening allows, otherwise select by
        /// PUCT on the ACTING side's own component (max^n), then back the leaf up unchanged.
        /// </summary>
        public static async Task<SearchNode> IterateAsync(SearchTree tree, float explorationC)
        {
            SearchNode node = tree.Root;
            while (true)
            {
                if (node.IsTerminal)
                {
                    SearchTree.Backup(node, node.LeafValues);
                    return node;
                }

                IReadOnlyList<UnitBranch> units = tree.UnitsOf(node);
                if (units.Count == 0)
                {
                    // No activatable unit here (the engine would end the round): value it as it stands.
                    SearchTree.Backup(node, node.LeafValues);
                    return node;
                }

                SearchNode? opened = await TryExpandAsync(tree, node, units);
                if (opened != null)
                {
                    SearchTree.Backup(opened, opened.LeafValues);
                    return opened;
                }

                SearchEdge? best = SelectPuct(node, explorationC);
                if (best?.Child == null)
                {
                    // Fully widened but nothing opened (every edge closed): this node is as far as
                    // the line goes.
                    SearchTree.Backup(node, node.LeafValues);
                    return node;
                }
                node = best.Child;
            }
        }

        /// <summary>
        /// PUCT on the acting side's own value (design sec 7.4):
        /// <c>Q(edge, actingSide) + c * prior * sqrt(parentVisits) / (1 + edgeVisits)</c>.
        /// Q is already in [0,1] (every evaluator's contract), so c needs no per-game rescaling.
        /// Deterministic: <see cref="SearchNode.OpenEdges"/> walks branches then edges in prior
        /// order and a strict &gt; keeps the first on a tie.
        /// </summary>
        public static SearchEdge? SelectPuct(SearchNode node, float explorationC)
        {
            int side = node.ActingSide;
            float parent = MathF.Sqrt(Math.Max(1, node.Visits));
            SearchEdge? best = null;
            float bestScore = float.NegativeInfinity;
            foreach (SearchEdge edge in node.OpenEdges())
            {
                float score = edge.QFor(side) + explorationC * edge.Prior * parent / (1 + edge.Visits);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = edge;
                }
            }
            return best;
        }

        /// <summary>
        /// Progressive widening at both levels, opening in PRIOR order (sec 4.1): a new unit branch
        /// when the node's own widening allows one, then the next untried edge of the first branch
        /// whose widening allows one. A closed edge (prescription fell through at play, or the line
        /// faulted) is never credited and never holds a slot - the next in prior order takes it.
        /// </summary>
        private static async Task<SearchNode?> TryExpandAsync(SearchTree tree, SearchNode node,
            IReadOnlyList<UnitBranch> units)
        {
            if (tree.CanOpenUnit(node) && tree.NextUnitToOpen(node) is { } next)
            {
                // The expensive step, paid once per (node, unit): MacroActionGenerator + Score.
                tree.EdgesOf(node, next);
            }

            foreach (UnitBranch unit in units)
            {
                while (tree.CanOpenEdge(unit))
                {
                    // Past the deadline nothing more is opened: every further open would be a
                    // simulation that stops at its first boundary, paid for at load cost each.
                    if (tree.Options.Cancellation.IsCancellationRequested) return null;

                    SearchEdge? edge = unit.NextUntried();
                    if (edge == null) break;
                    SearchNode? child = await tree.OpenAsync(node, unit, edge);
                    if (child != null) return child;
                }
            }
            return null;
        }

        // --- the search proper ----------------------------------------------------------------------

        /// <summary>
        /// Searches from a real activation-boundary snapshot: ONE probe establishes the root (the
        /// boundary state and the acting player are the same for every worker), then one tree per
        /// worker runs in parallel and their root statistics are merged.
        /// </summary>
        public static async Task<SearchResult> RunAsync(string snapshot, UctOptions options,
            IPositionEvaluator evaluator)
        {
            SearchTree.RootBoundary boundary;
            try
            {
                boundary = await SearchTree.ProbeRootAsync(snapshot, options.Tree, evaluator);
            }
            catch (SearchTree.SearchUnavailableException unavailable)
            {
                // A resumed game is a whole engine and can fault or time out before it reaches a
                // boundary - observed under a heavily loaded box. There is nothing to search, so the
                // search reports no choice and the caller falls back to the A-greedy policy (G3).
                // Never a crash: B5 runs this inside real games.
                return SearchResult.Unavailable(unavailable.Message, options.Iterations.HasValue);
            }
            // The hard deadline (#191 R9): the time budget used to be checked only between
            // iterations, so a single slow iteration (a 2k line in a Debug build on a laptop, or any
            // build under an attached debugger) overran it by its whole length, times the workers.
            // Armed with the budget once the root is measured, it stops every in-flight simulation at
            // its next boundary and opens nothing further; the workers then return at their next
            // clock check. Never armed under an iteration budget (G5).
            using var deadline = new CancellationTokenSource();
            return await RunAsync(workerSeed =>
            {
                SearchOptions treeOptions = options.Tree with { WorkerSeed = workerSeed, Cancellation = deadline.Token };
                return Task.FromResult(SearchTree.FromRoot(boundary, treeOptions,
                    new TacticianActionSpace(treeOptions), evaluator));
            }, options, deadline);
        }

        /// <summary>
        /// The core: <paramref name="treeForWorker"/> builds one worker's tree from its derived seed
        /// (tests author trees here; the real caller loads the snapshot). Workers share nothing.
        /// </summary>
        /// <param name="deadline">
        /// Armed with the time budget once the root is measured, if given; the trees must carry its
        /// token in <see cref="SearchOptions.Cancellation"/> for it to have any effect. Null (tests,
        /// authored trees) means the clock is checked only between iterations, as before.
        /// </param>
        public static async Task<SearchResult> RunAsync(Func<int, Task<SearchTree>> treeForWorker,
            UctOptions options, CancellationTokenSource? deadline = null)
        {
            var clock = Stopwatch.StartNew();
            int workerCount = Math.Max(1, options.Workers);

            var trees = new SearchTree[workerCount];
            var seeds = new int[workerCount];
            for (int worker = 0; worker < workerCount; worker++)
            {
                // Derived exactly like an edge's seed (sec 6), on the worker index, so two workers
                // never share a determinization and a worker is reproducible from the root seed.
                seeds[worker] = SearchSeeds.Derive(options.RootSeed, 0, 0, worker);
            }

            // Built in parallel: each tree costs one Probe (a resume + re-save at the boundary).
            await Task.WhenAll(Enumerable.Range(0, workerCount)
                .Select(async worker => trees[worker] = await treeForWorker(seeds[worker])));

            int rootUnits = trees[0].UnitsOf(trees[0].Root).Count;
            int budgetMs = options.BudgetMsFor(rootUnits);
            if (options.Iterations == null) deadline?.CancelAfter(budgetMs);

            var iterations = new int[workerCount];
            var searchClock = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, workerCount).Select(worker => Task.Run(async () =>
            {
                iterations[worker] = await RunWorkerAsync(trees[worker], options, searchClock, budgetMs);
            })));
            searchClock.Stop();
            clock.Stop();

            IReadOnlyList<RootEdgeStat> merged = MergeRoots(trees);
            RootEdgeStat? choice = ChooseRoot(merged);
            TreeShape shape = Measure(trees);

            return new SearchResult(choice, merged, iterations.Sum(), workerCount, clock.ElapsedMilliseconds,
                budgetMs, rootUnits, shape.Nodes, shape.ClosedEdges, shape.MaxDepth,
                options.Iterations.HasValue, choice == null ? "no edge could be opened" : "ok");
        }

        private static async Task<int> RunWorkerAsync(SearchTree tree, UctOptions options,
            Stopwatch clock, int budgetMs)
        {
            int done = 0;
            while (true)
            {
                if (options.Iterations is { } target)
                {
                    if (done >= target) break;
                }
                else if (clock.ElapsedMilliseconds >= budgetMs)
                {
                    break;
                }
                await IterateAsync(tree, options.ExplorationC);
                done++;
            }
            return done;
        }

        // --- root statistics ------------------------------------------------------------------------

        /// <summary>
        /// Sums every worker's root edges. Enumeration is deterministic given the snapshot (no RNG in
        /// the action space), so an edge is the same prescription in every tree and (unit, edge)
        /// index is a safe join key; workers differ only in WHICH edges they opened and how the
        /// sampled outcomes valued them.
        /// </summary>
        private static IReadOnlyList<RootEdgeStat> MergeRoots(IReadOnlyList<SearchTree> trees)
        {
            var merged = new Dictionary<(int Unit, int Edge), RootEdgeStat>();
            foreach (SearchTree tree in trees)
            {
                int side = tree.Root.ActingSide;
                if (tree.Root.Units == null) continue;
                foreach (UnitBranch unit in tree.Root.Units)
                {
                    if (unit.Edges == null) continue;
                    foreach (SearchEdge edge in unit.Edges)
                    {
                        if (edge.Child == null) continue;
                        (int, int) key = (unit.Index, edge.Index);
                        RootEdgeStat stat = merged.TryGetValue(key, out RootEdgeStat? existing)
                            ? existing
                            : new RootEdgeStat(unit.Index, edge.Index, edge.Label, edge.Prior,
                                edge.Prescription, 0, 0f);
                        merged[key] = stat with
                        {
                            Visits = stat.Visits + edge.Child.Visits,
                            ValueSum = stat.ValueSum + edge.Child.ValueSum[side],
                        };
                    }
                }
            }
            return merged.Values
                .OrderBy(s => s.UnitIndex).ThenBy(s => s.EdgeIndex)
                .ToList();
        }

        /// <summary>Most visits wins; ties go to the higher mean value, then to prior order.</summary>
        /// <summary>
        /// Robust child, applied the way the tree is shaped (#191 step 10, from the
        /// hold-the-responder probe): first the UNIT with the most visits in total, then the most
        /// visited edge under it. Choosing the single most visited edge across all units undercounted
        /// a unit whose macros are interchangeable - a last-round "spend the irrelevant unit" branch
        /// took 376 of 525 visits split five ways (80 each) and lost to the responder's one edge at 96,
        /// against the search's own values (0.19 vs 0.06). The level-1 decision is the activation;
        /// its visits are the evidence for it, however many equivalent ways of spending it exist.
        /// Ties: more visits, then higher value, then earlier (prior) order - unchanged.
        /// </summary>
        private static RootEdgeStat? ChooseRoot(IReadOnlyList<RootEdgeStat> stats)
        {
            if (stats.Count == 0) return null;
            var visitsByUnit = new Dictionary<int, int>();
            var valueByUnit = new Dictionary<int, float>();
            foreach (RootEdgeStat stat in stats)
            {
                visitsByUnit[stat.UnitIndex] = visitsByUnit.GetValueOrDefault(stat.UnitIndex) + stat.Visits;
                valueByUnit[stat.UnitIndex] = valueByUnit.GetValueOrDefault(stat.UnitIndex) + stat.ValueSum;
            }
            int bestUnit = -1;
            foreach (RootEdgeStat stat in stats)
            {
                int unit = stat.UnitIndex;
                if (bestUnit < 0) { bestUnit = unit; continue; }
                if (unit == bestUnit) continue;
                int v = visitsByUnit[unit], bv = visitsByUnit[bestUnit];
                float mean = v == 0 ? 0f : valueByUnit[unit] / v;
                float bestMean = bv == 0 ? 0f : valueByUnit[bestUnit] / bv;
                if (v > bv || (v == bv && mean > bestMean)) bestUnit = unit;
            }

            RootEdgeStat? best = null;
            foreach (RootEdgeStat stat in stats)
            {
                if (stat.UnitIndex != bestUnit) continue;
                if (best == null
                    || stat.Visits > best.Visits
                    || (stat.Visits == best.Visits && stat.Value > best.Value))
                {
                    best = stat;
                }
            }
            return best;
        }

        private readonly record struct TreeShape(int Nodes, int ClosedEdges, int MaxDepth);

        private static TreeShape Measure(IReadOnlyList<SearchTree> trees)
        {
            int nodes = 0, closed = 0, maxDepth = 0;
            foreach (SearchTree tree in trees)
            {
                var stack = new Stack<SearchNode>();
                stack.Push(tree.Root);
                while (stack.Count > 0)
                {
                    SearchNode node = stack.Pop();
                    nodes++;
                    if (node.Depth > maxDepth) maxDepth = node.Depth;
                    if (node.Units != null)
                    {
                        foreach (UnitBranch unit in node.Units)
                        {
                            if (unit.Edges == null) continue;
                            foreach (SearchEdge edge in unit.Edges)
                            {
                                if (edge.Closed) closed++;
                                if (edge.Child != null) stack.Push(edge.Child);
                            }
                        }
                    }
                }
            }
            return new TreeShape(nodes, closed, maxDepth);
        }
    }

    /// <summary>
    /// B4's knobs. The per-worker tree knobs stay in <see cref="SearchOptions"/> (widening,
    /// continuation, in-sim profile); this record holds what only a SEARCH has: the budget, the
    /// worker count, and the exploration constant.
    /// </summary>
    public sealed record UctOptions
    {
        /// <summary>Template for every worker's tree; its <see cref="SearchOptions.WorkerSeed"/> is overwritten per worker.</summary>
        public SearchOptions Tree { get; init; } = new();

        /// <summary>The search's seed: every worker seed, and through them every simulation seed, derives from it (G5).</summary>
        public int RootSeed { get; init; }

        /// <summary>Independent trees run in parallel; their root visits are summed (design sec 6).</summary>
        public int Workers { get; init; } = 1;

        /// <summary>PUCT exploration weight. Q is in [0,1] for every evaluator, so this needs no per-game rescaling.</summary>
        public float ExplorationC { get; init; } = 1.4f;

        /// <summary>
        /// Iterations PER WORKER. Set => the search is deterministic and ignores the clock (tests,
        /// reproducible measurements). Null => the time budget below applies.
        /// </summary>
        public int? Iterations { get; init; }

        /// <summary>
        /// Budget floor. The plan's figures: 1-2s in benches, 5-10s against humans - see
        /// <see cref="Benchmark"/> and <see cref="Interactive"/>.
        /// </summary>
        public int BaseBudgetMs { get; init; } = 1000;

        /// <summary>
        /// The campaign doc's rule: "time budget scales with root branching (a 4k game gets more
        /// time, not a shallower tree), within a hard cap". Root branching is measured as the number
        /// of activatable units at the root - level 1 is the cheap level, so this costs nothing.
        /// </summary>
        public int BudgetMsPerRootUnit { get; init; } = 120;

        /// <summary>The hard cap Chris sets for GUI play.</summary>
        public int MaxBudgetMs { get; init; } = 2000;

        public int BudgetMsFor(int rootUnits) =>
            Math.Clamp(BaseBudgetMs + BudgetMsPerRootUnit * Math.Max(0, rootUnits), BaseBudgetMs, MaxBudgetMs);

        /// <summary>Benchmarks: 1s floor, 2s cap (plan sec 9's "1-2s in benchmarks").</summary>
        public static UctOptions Benchmark => new() { BaseBudgetMs = 1000, MaxBudgetMs = 2000 };

        /// <summary>Against humans: 5s floor, 10s cap (plan sec 9's "5-10s vs humans").</summary>
        public static UctOptions Interactive => new()
        {
            BaseBudgetMs = 5000,
            BudgetMsPerRootUnit = 400,
            MaxBudgetMs = 10_000,
        };
    }

    /// <summary>One root edge's merged statistics across workers.</summary>
    public sealed record RootEdgeStat(int UnitIndex, int EdgeIndex, string Label, float Prior,
        SimulationService.Prescription Prescription, int Visits, float ValueSum)
    {
        /// <summary>Mean backed-up value for the root's acting side.</summary>
        public float Value => Visits == 0 ? 0f : ValueSum / Visits;
    }

    /// <summary>
    /// What one search produced. <see cref="Choice"/> is the prescription B5 will play; the rest is
    /// what the ledger and the benches read.
    /// </summary>
    public sealed record SearchResult(RootEdgeStat? Choice, IReadOnlyList<RootEdgeStat> Root,
        int Iterations, int Workers, long ElapsedMs, int BudgetMs, int RootUnits, int Nodes,
        int ClosedEdges, int MaxDepth, bool Deterministic, string Note)
    {
        /// <summary>No tree could be built (the root probe faulted): the caller falls back to A (G3).</summary>
        public static SearchResult Unavailable(string note, bool deterministic) =>
            new(null, Array.Empty<RootEdgeStat>(), 0, 0, 0, 0, 0, 0, 0, 0, deterministic, note);
    }
}
