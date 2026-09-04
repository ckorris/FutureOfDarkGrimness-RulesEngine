using FDG.Players;
using FDG.Simulation;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// Enumerates a node's composite edges in two levels (#191 B2 sec 3). The real one is
    /// <see cref="TacticianActionSpace"/>; tests author fixed ones.
    /// </summary>
    public interface IActionSpace
    {
        /// <summary>Level 1: the acting player's activatable units, in prior order (best first).</summary>
        IReadOnlyList<UnitBranch> EnumerateUnits(SearchNode node);

        /// <summary>Level 2: one unit's macro-action edges, in prior order (best first).</summary>
        IReadOnlyList<SearchEdge> EnumerateEdges(SearchNode node, UnitBranch unit);
    }

    /// <summary>
    /// What opening an edge produced (sec 4.2-4.3). <see cref="Honored"/> false means the prescription
    /// fell through at play and the edge must be closed, not credited. A Fault is reported as
    /// unhonored too (the line is discarded). Exactly one of <see cref="Snapshot"/> /
    /// <see cref="Terminal"/> is set on success.
    /// </summary>
    public sealed record ExpansionOutcome(string? Snapshot, PlayerID? ActingPlayer, GameResult? Terminal,
        SideValues? Leaf, bool Honored, string Note)
    {
        public bool Succeeded => Honored && (Snapshot != null || Terminal != null);
    }

    /// <summary>Creates a child by simulating one edge (sec 4.2). The real one runs a 5c line.</summary>
    public interface INodeExpander
    {
        Task<ExpansionOutcome> Expand(SearchNode parent, SearchEdge edge, int seed);
    }
}
