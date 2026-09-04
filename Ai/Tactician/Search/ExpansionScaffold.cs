namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// TEST SCAFFOLDING, not the search (#191 B2 sec 0; B4 owns the real loop). A fixed-count
    /// deterministic walk that exercises the tree end to end: at each node it opens the next child
    /// widening allows, in prior order, and otherwise descends into the opened edge with the best
    /// value FOR THE ACTING SIDE (max^n, sec 7.4); the reached leaf's values are backed up.
    /// No exploration term, no time budget, no parallelism - none of which B2 decides.
    /// </summary>
    public static class ExpansionScaffold
    {
        public static async Task RunAsync(SearchTree tree, int iterations)
        {
            for (int i = 0; i < iterations; i++) await IterateAsync(tree);
        }

        /// <summary>One descent from the root to a leaf, with at most one expansion, then a backup.</summary>
        public static async Task<SearchNode> IterateAsync(SearchTree tree)
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
                    SearchTree.Backup(node, node.LeafValues);
                    return node;
                }

                // Expansion: the first branch (prior order) that widening lets us open one more edge
                // in. A branch whose edges are not yet enumerated counts as a NEW child of the node,
                // so it is gated by the node's own widening.
                SearchNode? opened = await TryExpandAsync(tree, node, units);
                if (opened != null)
                {
                    SearchTree.Backup(opened, opened.LeafValues);
                    return opened;
                }

                // Exploitation: descend into the acting side's best opened edge.
                SearchEdge? best = null;
                foreach (SearchEdge edge in node.OpenEdges())
                {
                    if (best == null || edge.QFor(node.ActingSide) > best.QFor(node.ActingSide)) best = edge;
                }
                if (best?.Child == null)
                {
                    SearchTree.Backup(node, node.LeafValues);
                    return node;
                }
                node = best.Child;
            }
        }

        private static async Task<SearchNode?> TryExpandAsync(SearchTree tree, SearchNode node,
            IReadOnlyList<UnitBranch> units)
        {
            foreach (UnitBranch unit in units)
            {
                if (unit.Edges == null)
                {
                    if (!tree.CanOpenUnit(node)) return null;
                    tree.EdgesOf(node, unit);
                }
                while (tree.CanOpenEdge(unit))
                {
                    SearchEdge? next = unit.NextUntried();
                    if (next == null) break;
                    SearchNode? child = await tree.OpenAsync(node, unit, next);
                    if (child != null) return child;
                    // Closed edge: not credited; the next in prior order takes its slot.
                }
            }
            return null;
        }
    }
}
