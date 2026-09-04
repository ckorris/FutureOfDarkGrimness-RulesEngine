using FDG.Data;
using FDG.SaveLoad;
using FDG.Simulation;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// The tree (#191 B2): nodes, two-level edges, progressive widening, child creation, max^n
    /// backup. NOT the search loop - B4 owns selection, time budgets and root parallelism and is
    /// written against <see cref="SearchEdge.QFor"/> / <see cref="SearchEdge.Prior"/> and the widening
    /// queries here (<see cref="ExpansionScaffold"/> is the test-only loop that exercises this).
    /// </summary>
    public sealed class SearchTree
    {
        public SearchNode Root { get; }
        public SideMap Sides { get; }
        public SearchOptions Options { get; }

        private readonly IActionSpace _space;
        private readonly INodeExpander _expander;

        public SearchTree(SearchNode root, SideMap sides, SearchOptions options,
            IActionSpace space, INodeExpander expander)
        {
            Root = root;
            Sides = sides;
            Options = options;
            _space = space;
            _expander = expander;
        }

        /// <summary>
        /// A tree rooted at a snapshot: the engine itself says who is about to activate there
        /// (<see cref="SimulationService.Probe"/>), and the root's snapshot is the engine's re-saved
        /// state at that boundary so every child is consistent with it.
        /// </summary>
        public static async Task<SearchTree> FromSnapshotAsync(string snapshot, SearchOptions options,
            IActionSpace space, IPositionEvaluator evaluator)
        {
            var probeService = new SimulationService(new SimulationService.SimulationOptions
            {
                Profile = options.InSimProfile,
                Seed = options.WorkerSeed,
                Randomness = options.Randomness,
                TimeoutSeconds = options.TimeoutSeconds,
            });
            SimulationService.SimulationResult probe = await probeService.Probe(snapshot);
            if (!probe.ReachedEndOfLine || probe.ActingPlayerAtEnd is not { } acting)
                throw new InvalidOperationException($"SearchTree: the root snapshot has no activation boundary ({probe.Note}).");

            GameDataStore store = GameSaveSerializer.Load(probe.Snapshot!);
            SideMap sides = SideMap.FromStore(store);
            // A plain rule evaluator for the leaf evaluator's own use (#191 B3): never rolls, so an
            // unseeded dice roller behind it is inert - see IPositionEvaluator's contract note.
            var ruleEvaluator = new Rules.Dispatch.RuleEvaluator(new ProbabilisticDiceRoller());
            SideValues rootEstimate = evaluator.Evaluate(new TableState(store), ruleEvaluator, sides);
            var root = new SearchNode(probe.Snapshot, acting, sides.SideOf(acting), null, rootEstimate,
                depth: 0, parent: null, parentEdge: null);
            return new SearchTree(root, sides, options, space, new SimulationExpander(options, evaluator, sides));
        }

        // --- widening (sec 4.1) ------------------------------------------------------------------

        /// <summary>Enumerates the node's level-1 branches on first use.</summary>
        public IReadOnlyList<UnitBranch> UnitsOf(SearchNode node)
        {
            if (node.IsTerminal) return Array.Empty<UnitBranch>();
            node.Units ??= _space.EnumerateUnits(node).ToList();
            return node.Units;
        }

        /// <summary>Whether widening lets this node open one more unit branch (enumerate its edges).</summary>
        public bool CanOpenUnit(SearchNode node)
        {
            IReadOnlyList<UnitBranch> units = UnitsOf(node);
            int opened = units.Count(u => u.Edges != null);
            return opened < units.Count && opened < Options.AllowedChildren(node.Visits);
        }

        /// <summary>The next unit branch in prior order whose edges are not enumerated yet.</summary>
        public UnitBranch? NextUnitToOpen(SearchNode node) => UnitsOf(node).FirstOrDefault(u => u.Edges == null);

        /// <summary>Enumerates a branch's edges (the expensive step, once per (node, unit)).</summary>
        public IReadOnlyList<SearchEdge> EdgesOf(SearchNode node, UnitBranch unit)
        {
            if (unit.Edges == null)
            {
                unit.Edges = _space.EnumerateEdges(node, unit).ToList();
                if (node.Units != null && node.Units.All(u => u.Edges != null)) node.ReleaseScratch();
            }
            return unit.Edges;
        }

        /// <summary>Whether widening lets this branch try one more edge.</summary>
        public bool CanOpenEdge(UnitBranch unit)
        {
            if (unit.Edges == null || unit.NextUntried() == null) return false;
            // Closed edges do not hold a widening slot (sec 4.3): the next in prior order takes it.
            int opened = unit.Edges.Count(e => e.Child != null);
            return opened < Options.AllowedChildren(unit.Visits);
        }

        // --- child creation (sec 4.2-4.3, 5.2-5.3, 6) -----------------------------------------------

        /// <summary>
        /// Opens an edge: one line under a seed derived from (worker, depth, unit, edge). Returns the
        /// child, or null when the edge was closed (fell through, faulted, timed out) - a closed edge
        /// is never credited and the caller moves to the next in prior order.
        /// </summary>
        public async Task<SearchNode?> OpenAsync(SearchNode node, UnitBranch unit, SearchEdge edge)
        {
            if (edge.Child != null) return edge.Child;
            if (edge.Closed) return null;

            int seed = SearchSeeds.Derive(Options.WorkerSeed, node.Depth, unit.Index, edge.Index);
            ExpansionOutcome outcome = await _expander.Expand(node, edge, seed);
            if (!outcome.Succeeded)
            {
                edge.Closed = true;
                edge.ClosedReason = outcome.Note;
                return null;
            }

            SearchNode child;
            if (outcome.Terminal is { } terminal)
            {
                // A terminal node has no acting player; it inherits the parent's for bookkeeping.
                child = new SearchNode(null, node.ActingPlayer, node.ActingSide, terminal,
                    SideValues.FromResult(terminal, Sides), node.Depth + 1, node, edge);
            }
            else
            {
                if (outcome.ActingPlayer is not { } acting || outcome.Leaf == null)
                    throw new InvalidOperationException("SearchTree: a non-terminal expansion must report its acting player and leaf value.");
                child = new SearchNode(outcome.Snapshot, acting, Sides.SideOf(acting), null, outcome.Leaf,
                    node.Depth + 1, node, edge);
            }
            edge.Child = child;
            return child;
        }

        // --- backup (sec 7.3) ---------------------------------------------------------------------

        /// <summary>Adds the leaf's values unchanged to every node from the leaf to the root.</summary>
        public static void Backup(SearchNode leaf, SideValues values)
        {
            for (SearchNode? node = leaf; node != null; node = node.Parent)
            {
                node.Visits++;
                node.ValueSum.AddInPlace(values);
            }
        }

        /// <summary>
        /// The root's answer: the opened root edge with the most visits (ties: the acting side's Q,
        /// then prior order). Null before any child exists.
        /// </summary>
        public SearchEdge? RootChoice()
        {
            SearchEdge? best = null;
            foreach (SearchEdge edge in Root.OpenEdges())
            {
                if (best == null
                    || edge.Visits > best.Visits
                    || (edge.Visits == best.Visits && edge.QFor(Root.ActingSide) > best.QFor(Root.ActingSide)))
                {
                    best = edge;
                }
            }
            return best;
        }
    }
}
