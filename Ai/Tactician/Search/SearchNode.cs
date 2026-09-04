using FDG.Data;
using FDG.Players;
using FDG.Simulation;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// A tree node: "player P is about to activate" at a 5c activation boundary (#191 B2 sec 2).
    /// <see cref="ActingPlayer"/> and <see cref="ActingSide"/> come from the engine's own
    /// determination at that boundary (the line reports it), never from the parent - reactivations
    /// and P19 overrides break alternation, and the tree follows the engine.
    /// </summary>
    public sealed class SearchNode
    {
        /// <summary>The engine's saved state at this boundary; null only for a terminal node.</summary>
        public string? Snapshot { get; }

        public PlayerID ActingPlayer { get; }

        public int ActingSide { get; }

        /// <summary>Set when the line reached the game's end: no children, fixed value (sec 7.1).</summary>
        public GameResult? Terminal { get; }

        /// <summary>
        /// The evaluator's estimate of this position, taken live at the boundary the creating line
        /// stopped at (sec 5.2); the terminal values for a terminal node. What gets backed up when
        /// this node is the leaf of a descent.
        /// </summary>
        public SideValues LeafValues { get; }

        public int Depth { get; }

        public SearchNode? Parent { get; }

        public SearchEdge? ParentEdge { get; }

        public int Visits { get; internal set; }

        public SideValues ValueSum { get; }

        /// <summary>Level-1 children in prior order; null until the action space has enumerated them.</summary>
        public List<UnitBranch>? Units { get; internal set; }

        /// <summary>
        /// The action space's cached loaded store for this node (a load is ~30-50ms at 2k, and level-1
        /// and level-2 enumeration happen at different times). Released by the tree once every unit
        /// branch has its edges; memory is measured at B4's soak (sec 5.3).
        /// </summary>
        internal object? Scratch { get; set; }

        public bool IsTerminal => Terminal != null;

        public SearchNode(string? snapshot, PlayerID actingPlayer, int actingSide, GameResult? terminal,
            SideValues leafValues, int depth, SearchNode? parent, SearchEdge? parentEdge)
        {
            Snapshot = snapshot;
            ActingPlayer = actingPlayer;
            ActingSide = actingSide;
            Terminal = terminal;
            LeafValues = leafValues;
            Depth = depth;
            Parent = parent;
            ParentEdge = parentEdge;
            ValueSum = new SideValues(leafValues.Count);
        }

        /// <summary>Mean backed-up value for a side, 0 before any visit.</summary>
        public float QFor(int side) => Visits == 0 ? 0f : ValueSum[side] / Visits;

        /// <summary>Every edge with a live child, across all enumerated unit branches, in branch/prior order.</summary>
        public IEnumerable<SearchEdge> OpenEdges()
        {
            if (Units == null) yield break;
            foreach (UnitBranch unit in Units)
            {
                if (unit.Edges == null) continue;
                foreach (SearchEdge edge in unit.Edges)
                    if (edge.Child != null) yield return edge;
            }
        }

        internal void ReleaseScratch() => Scratch = null;
    }

    /// <summary>
    /// Level 1 of the composite edge (sec 3.1): which unit. Ordered by the A policy's own activation
    /// ranking, so the first branch opened is A's unit. Its macro-action edges are enumerated lazily
    /// (sec 3.2) - that is the expensive step, paid once per (node, unit).
    /// </summary>
    public sealed class UnitBranch
    {
        public int Index { get; }
        public DataReference Unit { get; }
        public string Name { get; }
        public float Prior { get; }

        /// <summary>Level-2 edges in prior order; null until enumerated.</summary>
        public List<SearchEdge>? Edges { get; internal set; }

        public UnitBranch(int index, DataReference unit, string name, float prior)
        {
            Index = index;
            Unit = unit;
            Name = name;
            Prior = prior;
        }

        /// <summary>Edges tried so far: opened (have a child) or closed (fell through / faulted).</summary>
        public int TriedEdges => Edges?.Count(e => e.Child != null || e.Closed) ?? 0;

        public int Visits => Edges?.Sum(e => e.Visits) ?? 0;

        /// <summary>The next edge in prior order that has neither a child nor been closed.</summary>
        public SearchEdge? NextUntried() => Edges?.FirstOrDefault(e => e.Child == null && !e.Closed);
    }

    /// <summary>
    /// Level 2 of the composite edge (sec 3.2): the (unit, action, macro-action) tuple as a
    /// <see cref="SimulationService.Prescription"/>, its prior, and its child once opened. Value
    /// accounting lives on the child node; <see cref="QFor"/> and <see cref="Prior"/> are the two
    /// things B4's selection formula reads (sec 7.4).
    /// </summary>
    public sealed class SearchEdge
    {
        public int Index { get; }
        public SimulationService.Prescription Prescription { get; }
        public float Prior { get; }
        public string Label { get; }

        public SearchNode? Child { get; internal set; }

        /// <summary>Closed edges are never credited and never reopened (sec 4.3): the prescription fell through, or the line faulted.</summary>
        public bool Closed { get; internal set; }
        public string? ClosedReason { get; internal set; }

        public SearchEdge(int index, SimulationService.Prescription prescription, float prior, string label)
        {
            Index = index;
            Prescription = prescription;
            Prior = prior;
            Label = label;
        }

        public int Visits => Child?.Visits ?? 0;

        /// <summary>Exploitation term for a side: the child's mean backed-up value for it (sec 7.4).</summary>
        public float QFor(int side) => Child?.QFor(side) ?? 0f;
    }
}
