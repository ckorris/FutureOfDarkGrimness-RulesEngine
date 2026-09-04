using FDG.Ai.Tactician.Search;
using FDG.Players;
using FDG.Simulation;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #191 B2 (docs/tactician-b2-design.md) - the tree's backup and widening rules on AUTHORED
    /// trees with fixed leaf values, no engine: max^n reduces to minimax in 1v1 (spec test 5),
    /// teammates share a component, a third side maximizes its own value rather than minimizing the
    /// root's, closed edges are never credited, widening opens in prior order.
    /// </summary>
    [TestFixture]
    public class SearchTreeTests
    {
        // --- authored tree plumbing -------------------------------------------------------------

        private sealed record AuthoredEdge(string Label, float Prior, string? ChildKey, float[] Values,
            bool Honored = true);

        private sealed record AuthoredUnit(string Name, float Prior, List<AuthoredEdge> Edges);

        private sealed class AuthoredTree : IActionSpace, INodeExpander
        {
            public readonly Dictionary<string, List<AuthoredUnit>> Units = new();
            public readonly Dictionary<string, PlayerID> ActingPlayerOf = new();
            public readonly List<(string Parent, string Edge, int Seed)> Expansions = new();

            public IReadOnlyList<UnitBranch> EnumerateUnits(SearchNode node)
            {
                if (!Units.TryGetValue(node.Snapshot!, out List<AuthoredUnit>? units)) return Array.Empty<UnitBranch>();
                return units.Select((u, i) => new UnitBranch(i, default, u.Name, u.Prior)).ToList();
            }

            public IReadOnlyList<SearchEdge> EnumerateEdges(SearchNode node, UnitBranch unit) =>
                Units[node.Snapshot!][unit.Index].Edges
                    .Select((e, i) => new SearchEdge(i, new SimulationService.Prescription(null), e.Prior, e.Label))
                    .ToList();

            public Task<ExpansionOutcome> Expand(SearchNode parent, SearchEdge edge, int seed)
            {
                Expansions.Add((parent.Snapshot!, edge.Label, seed));
                AuthoredEdge authored = Units[parent.Snapshot!].SelectMany(u => u.Edges).First(e => e.Label == edge.Label);
                if (!authored.Honored)
                    return Task.FromResult(new ExpansionOutcome(null, null, null, null, false, "authored fall-through"));
                return Task.FromResult(new ExpansionOutcome(authored.ChildKey, ActingPlayerOf[authored.ChildKey!],
                    null, new SideValues(authored.Values), true, "ok"));
            }
        }

        private static SearchTree Build(AuthoredTree authored, SideMap sides, string rootKey, SearchOptions? options = null)
        {
            PlayerID acting = authored.ActingPlayerOf[rootKey];
            var root = new SearchNode(rootKey, acting, sides.SideOf(acting), null,
                SideValues.Uniform(sides.Count, 0.5f), 0, null, null);
            return new SearchTree(root, sides, options ?? new SearchOptions(), authored, authored);
        }

        private static readonly PlayerID P0 = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        private static readonly PlayerID P1 = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        private static readonly PlayerID P2 = new(Guid.Parse("00000000-0000-0000-0000-000000000003"));
        private static readonly PlayerID P3 = new(Guid.Parse("00000000-0000-0000-0000-000000000004"));

        private static SearchNode NodeByKey(SearchTree tree, string key)
        {
            var stack = new Stack<SearchNode>();
            stack.Push(tree.Root);
            while (stack.Count > 0)
            {
                SearchNode node = stack.Pop();
                if (node.Snapshot == key) return node;
                foreach (SearchEdge edge in node.OpenEdges()) stack.Push(edge.Child!);
            }
            throw new KeyNotFoundException(key);
        }

        // --- (1) max^n == minimax in 1v1 ----------------------------------------------------------

        // A two-ply 1v1 tree: root (side 0) -> 4 replies (side 1) -> 2 leaves each (side 0). Leaf
        // values are complementary (v1 = 1 - v0), as every shipped evaluator must be.
        private static AuthoredTree TwoPlyOneVsOne(out SideMap sides)
        {
            sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1) });
            var t = new AuthoredTree();
            t.ActingPlayerOf["root"] = P0;
            float[] V(float v0) => new[] { v0, 1f - v0 };
            // Root children keyed A..D; their leaves keyed A1, A2, ...
            var leafValues = new Dictionary<string, float>
            {
                ["A1"] = 0.70f, ["A2"] = 0.20f, // reply A: opponent will steer to A2 -> 0.20 for us
                ["B1"] = 0.55f, ["B2"] = 0.50f, // reply B: worst case 0.50 - the minimax pick
                ["C1"] = 0.90f, ["C2"] = 0.10f,
                ["D1"] = 0.45f, ["D2"] = 0.40f,
            };
            t.Units["root"] = new()
            {
                new AuthoredUnit("u1", 0.6f, new()
                {
                    new AuthoredEdge("A", 0.5f, "A", V(0.6f)),
                    new AuthoredEdge("B", 0.5f, "B", V(0.5f)),
                }),
                new AuthoredUnit("u2", 0.4f, new()
                {
                    new AuthoredEdge("C", 0.5f, "C", V(0.5f)),
                    new AuthoredEdge("D", 0.5f, "D", V(0.4f)),
                }),
            };
            foreach (string reply in new[] { "A", "B", "C", "D" })
            {
                t.ActingPlayerOf[reply] = P1;
                t.Units[reply] = new()
                {
                    new AuthoredUnit("opp", 1f, new()
                    {
                        new AuthoredEdge(reply + "1", 0.5f, reply + "1", V(leafValues[reply + "1"])),
                        new AuthoredEdge(reply + "2", 0.5f, reply + "2", V(leafValues[reply + "2"])),
                    }),
                };
                t.ActingPlayerOf[reply + "1"] = P0;
                t.ActingPlayerOf[reply + "2"] = P0;
            }
            return t;
        }

        /// <summary>
        /// The scalar reference: the SAME walk as ExpansionScaffold, but every node stores a single
        /// number (side 0's value) and a side-1 node reads 1 - v. If the vector machinery is right,
        /// the two produce identical visit counts everywhere and the same root choice.
        /// </summary>
        private sealed class ScalarReference
        {
            private sealed class Node
            {
                public string Key = "";
                public int Side;
                public float Leaf;
                public int Visits;
                public float Sum;
                public List<(string Label, float Prior, Node? Child, bool Tried)>[]? Units;
                public int[] UnitTried = Array.Empty<int>();
            }

            private readonly AuthoredTree _authored;
            private readonly SideMap _sides;
            private readonly SearchOptions _options;
            private readonly Node _root;
            public readonly Dictionary<string, int> Visits = new();

            public ScalarReference(AuthoredTree authored, SideMap sides, string rootKey, SearchOptions options)
            {
                _authored = authored;
                _sides = sides;
                _options = options;
                _root = new Node { Key = rootKey, Side = sides.SideOf(authored.ActingPlayerOf[rootKey]), Leaf = 0.5f };
            }

            private static float Read(Node n, int side) => n.Visits == 0 ? 0f : (side == 0 ? n.Sum : n.Visits - n.Sum) / n.Visits;

            public string? Run(int iterations)
            {
                for (int i = 0; i < iterations; i++) Iterate();
                Collect(_root);
                // Root choice: most visits, then own-side Q.
                (string Label, Node Child)? best = null;
                foreach (var unit in _root.Units ?? Array.Empty<List<(string, float, Node?, bool)>>())
                    foreach ((string label, float _, Node? child, bool _) in unit)
                        if (child != null && (best == null || child.Visits > best.Value.Child.Visits
                            || (child.Visits == best.Value.Child.Visits && Read(child, _root.Side) > Read(best.Value.Child, _root.Side))))
                            best = (label, child);
                return best?.Label;
            }

            private void Collect(Node n)
            {
                Visits[n.Key] = n.Visits;
                foreach (var unit in n.Units ?? Array.Empty<List<(string, float, Node?, bool)>>())
                    foreach ((_, _, Node? child, _) in unit)
                        if (child != null) Collect(child);
            }

            private void Backup(Node leaf, float v0, List<Node> path)
            {
                foreach (Node n in path) { n.Visits++; n.Sum += v0; }
                leaf.Visits++; leaf.Sum += v0;
            }

            private void Iterate()
            {
                var path = new List<Node>();
                Node node = _root;
                while (true)
                {
                    if (!_authored.Units.TryGetValue(node.Key, out List<AuthoredUnit>? authoredUnits) || authoredUnits.Count == 0)
                    {
                        Backup(node, node.Leaf, path);
                        return;
                    }
                    node.Units ??= authoredUnits.Select(u => (List<(string, float, Node?, bool)>?)null).ToArray()!;
                    if (node.UnitTried.Length == 0) node.UnitTried = new int[authoredUnits.Count];

                    // Expansion in prior order under the same widening.
                    bool expanded = false;
                    for (int u = 0; u < authoredUnits.Count && !expanded; u++)
                    {
                        if (node.Units[u] == null)
                        {
                            int openedUnits = node.Units.Count(x => x != null);
                            if (openedUnits >= _options.AllowedChildren(node.Visits)) break;
                            node.Units[u] = authoredUnits[u].Edges.Select(e => (e.Label, e.Prior, (Node?)null, false)).ToList();
                        }
                        var edges = node.Units[u]!;
                        while (true)
                        {
                            int tried = edges.Count(e => e.Tried);
                            int unitVisits = edges.Where(e => e.Child != null).Sum(e => e.Child!.Visits);
                            if (tried >= edges.Count || tried >= _options.AllowedChildren(unitVisits)) break;
                            int next = edges.FindIndex(e => !e.Tried);
                            AuthoredEdge authored = authoredUnits[u].Edges[next];
                            if (!authored.Honored)
                            {
                                edges[next] = (edges[next].Label, edges[next].Prior, null, true);
                                continue;
                            }
                            var child = new Node
                            {
                                Key = authored.ChildKey!,
                                Side = _sides.SideOf(_authored.ActingPlayerOf[authored.ChildKey!]),
                                Leaf = authored.Values[0],
                            };
                            edges[next] = (edges[next].Label, edges[next].Prior, child, true);
                            path.Add(node);
                            Backup(child, child.Leaf, path);
                            expanded = true;
                            break;
                        }
                    }
                    if (expanded) return;

                    Node? best = null;
                    foreach (var unit in node.Units)
                        foreach ((_, _, Node? child, _) in unit ?? new())
                            if (child != null && (best == null || Read(child, node.Side) > Read(best, node.Side))) best = child;
                    if (best == null)
                    {
                        Backup(node, node.Leaf, path);
                        return;
                    }
                    path.Add(node);
                    node = best;
                }
            }
        }

        [Test]
        public async Task OneVsOne_VectorBackupMatchesScalarNegamax_VisitForVisit()
        {
            AuthoredTree authored = TwoPlyOneVsOne(out SideMap sides);
            var options = new SearchOptions { WideningC = 2f, WideningAlpha = 0.5f };
            SearchTree tree = Build(authored, sides, "root", options);
            await ExpansionScaffold.RunAsync(tree, 24);

            var scalar = new ScalarReference(authored, sides, "root", options);
            string? scalarChoice = scalar.Run(24);

            foreach ((string key, int visits) in scalar.Visits)
            {
                SearchNode node = NodeByKey(tree, key);
                Assert.That(node.Visits, Is.EqualTo(visits), $"visits at {key} must match the scalar reference");
            }
            Assert.That(tree.RootChoice()!.Label, Is.EqualTo(scalarChoice), "root choice must match the scalar reference");
            // And the choice is the minimax one: reply B's worst case (0.50) beats A's (0.20), C's (0.10), D's (0.40).
            Assert.That(tree.RootChoice()!.Label, Is.EqualTo("B"));
        }

        [Test]
        public async Task OneVsOne_OpponentNodeSteersToOurWorstCase()
        {
            AuthoredTree authored = TwoPlyOneVsOne(out SideMap sides);
            SearchTree tree = Build(authored, sides, "root", new SearchOptions { WideningC = 2f, WideningAlpha = 0.5f });
            await ExpansionScaffold.RunAsync(tree, 24);

            // Under reply A the opponent (side 1) descends into A2 (0.20 for us = 0.80 for them), not A1.
            SearchNode a = NodeByKey(tree, "A");
            SearchEdge a1 = a.OpenEdges().First(e => e.Label == "A1");
            SearchEdge a2 = a.OpenEdges().First(e => e.Label == "A2");
            Assert.That(a2.Visits, Is.GreaterThan(a1.Visits), "the opponent's node must maximize ITS side, i.e. minimize ours");
        }

        // --- (2) teammates share a component; (3) a third side maximizes its own --------------------

        [Test]
        public async Task TwoVsTwo_TeammateNodeReadsTheSameComponentAsTheRoot()
        {
            SideMap sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1), (P2, 0), (P3, 1) });
            var t = new AuthoredTree();
            t.ActingPlayerOf["root"] = P0;
            t.ActingPlayerOf["A"] = P2; // the root's TEAMMATE acts next
            t.ActingPlayerOf["G1"] = P1;
            t.ActingPlayerOf["G2"] = P1;
            t.Units["root"] = new() { new AuthoredUnit("u", 1f, new() { new AuthoredEdge("A", 1f, "A", new[] { 0.5f, 0.5f }) }) };
            t.Units["A"] = new()
            {
                new AuthoredUnit("mate", 1f, new()
                {
                    new AuthoredEdge("G1", 0.5f, "G1", new[] { 0.8f, 0.2f }),
                    new AuthoredEdge("G2", 0.5f, "G2", new[] { 0.3f, 0.7f }),
                }),
            };
            SearchTree tree = Build(t, sides, "root", new SearchOptions { WideningC = 2f, WideningAlpha = 0.5f });
            await ExpansionScaffold.RunAsync(tree, 12);

            SearchNode a = NodeByKey(tree, "A");
            Assert.That(a.ActingSide, Is.EqualTo(tree.Root.ActingSide), "teammates are the same side");
            SearchEdge g1 = a.OpenEdges().First(e => e.Label == "G1");
            SearchEdge g2 = a.OpenEdges().First(e => e.Label == "G2");
            Assert.That(g1.Visits, Is.GreaterThan(g2.Visits), "the teammate's node must steer toward the SHARED side's best");
        }

        [Test]
        public async Task ThreeSides_ANonRootSideMaximizesItsOwnValue_NotTheRootsWorstCase()
        {
            SideMap sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1), (P2, 2) });
            var t = new AuthoredTree();
            t.ActingPlayerOf["root"] = P0;
            t.ActingPlayerOf["A"] = P2; // side 2 acts
            t.ActingPlayerOf["G1"] = P0;
            t.ActingPlayerOf["G2"] = P0;
            t.Units["root"] = new() { new AuthoredUnit("u", 1f, new() { new AuthoredEdge("A", 1f, "A", new[] { 0.3f, 0.3f, 0.4f }) }) };
            // G1 is best for side 2 (0.4 > 0.3) AND fine for the root; G2 is worst for the root. A
            // minimizer of the root's value picks G2; max^n picks G1.
            t.Units["A"] = new()
            {
                new AuthoredUnit("third", 1f, new()
                {
                    new AuthoredEdge("G1", 0.5f, "G1", new[] { 0.6f, 0.0f, 0.4f }),
                    new AuthoredEdge("G2", 0.5f, "G2", new[] { 0.0f, 0.7f, 0.3f }),
                }),
            };
            SearchTree tree = Build(t, sides, "root", new SearchOptions { WideningC = 2f, WideningAlpha = 0.5f });
            await ExpansionScaffold.RunAsync(tree, 12);

            SearchNode a = NodeByKey(tree, "A");
            SearchEdge g1 = a.OpenEdges().First(e => e.Label == "G1");
            SearchEdge g2 = a.OpenEdges().First(e => e.Label == "G2");
            Assert.That(g1.Visits, Is.GreaterThan(g2.Visits),
                "a third side maximizes its own component (max^n), it does not minimize the root's");
        }

        // --- closed edges, widening order, seeds ----------------------------------------------------

        [Test]
        public async Task FellThroughEdge_IsClosedAndNeverCredited_AndTheNextInPriorOrderOpens()
        {
            SideMap sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1) });
            var t = new AuthoredTree();
            t.ActingPlayerOf["root"] = P0;
            t.ActingPlayerOf["B"] = P1;
            t.Units["root"] = new()
            {
                new AuthoredUnit("u", 1f, new()
                {
                    new AuthoredEdge("A", 0.9f, "A", new[] { 0.9f, 0.1f }, Honored: false),
                    new AuthoredEdge("B", 0.1f, "B", new[] { 0.4f, 0.6f }),
                }),
            };
            SearchTree tree = Build(t, sides, "root", new SearchOptions { WideningC = 1f, WideningAlpha = 0f });
            SearchNode leaf = await ExpansionScaffold.IterateAsync(tree);

            SearchEdge a = tree.Root.Units![0].Edges!.First(e => e.Label == "A");
            SearchEdge b = tree.Root.Units![0].Edges!.First(e => e.Label == "B");
            Assert.That(a.Closed, Is.True, "an unhonored prescription closes its edge");
            Assert.That(a.Child, Is.Null);
            Assert.That(a.Visits, Is.EqualTo(0), "a closed edge is never credited");
            Assert.That(b.Child, Is.SameAs(leaf), "the next edge in prior order took the slot in the SAME iteration");
            Assert.That(tree.Root.Visits, Is.EqualTo(1));
            Assert.That(tree.Root.ValueSum[0], Is.EqualTo(0.4f).Within(1e-6f), "only the honored child's value reached the root");
        }

        [Test]
        public async Task Widening_OpensChildrenInPriorOrderAndGrowsWithVisits()
        {
            SideMap sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1) });
            var t = new AuthoredTree();
            t.ActingPlayerOf["root"] = P0;
            t.Units["root"] = new()
            {
                new AuthoredUnit("u1", 0.7f, new()
                {
                    new AuthoredEdge("E1", 0.6f, "E1", new[] { 0.5f, 0.5f }),
                    new AuthoredEdge("E2", 0.4f, "E2", new[] { 0.5f, 0.5f }),
                }),
                new AuthoredUnit("u2", 0.3f, new()
                {
                    new AuthoredEdge("E3", 1f, "E3", new[] { 0.5f, 0.5f }),
                }),
            };
            foreach (string key in new[] { "E1", "E2", "E3" }) t.ActingPlayerOf[key] = P1;
            var options = new SearchOptions { WideningC = 2f, WideningAlpha = 0.5f };
            SearchTree tree = Build(t, sides, "root", options);

            // k(0) = 1: the first iteration may open exactly one unit and one edge - A's own move.
            SearchNode first = await ExpansionScaffold.IterateAsync(tree);
            Assert.That(first.Snapshot, Is.EqualTo("E1"));
            Assert.That(t.Expansions.Select(e => e.Edge), Is.EqualTo(new[] { "E1" }));

            // k(1) = 2: the second may open one more - the next in prior order at whichever level.
            await ExpansionScaffold.IterateAsync(tree);
            Assert.That(t.Expansions.Select(e => e.Edge), Is.EqualTo(new[] { "E1", "E2" }));

            // Seeds are derived, distinct per (depth, unit, edge), and reproducible.
            Assert.That(t.Expansions[0].Seed, Is.Not.EqualTo(t.Expansions[1].Seed));
            Assert.That(t.Expansions[0].Seed, Is.EqualTo(SearchSeeds.Derive(options.WorkerSeed, 0, 0, 0)));
            Assert.That(SearchSeeds.Derive(7, 0, 0, 0), Is.Not.EqualTo(SearchSeeds.Derive(8, 0, 0, 0)),
                "two workers never share a determinization");
        }

        [Test]
        public void SideValues_TerminalFromResult_WinTieAndFault()
        {
            SideMap sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1), (P2, 0) });
            var scores = new List<PlayerObjectiveScore>();
            GameResult win = GameResult.ForWin(new[] { P1 }, new[] { "Blue" }, scores, 4);
            SideValues v = SideValues.FromResult(win, sides);
            Assert.That(v[1], Is.EqualTo(1f));
            Assert.That(v[0], Is.EqualTo(0f));
            Assert.That(v.IsComplementaryTwoSide(), Is.True);

            SideValues tie = SideValues.FromResult(GameResult.ForTie(scores, 4), sides);
            Assert.That(tie[0], Is.EqualTo(0.5f));
            Assert.That(tie[1], Is.EqualTo(0.5f));

            Assert.Throws<ArgumentException>(() => SideValues.FromResult(GameResult.ForFault("boom"), sides),
                "a faulted line is not a node");
        }
    }
}
