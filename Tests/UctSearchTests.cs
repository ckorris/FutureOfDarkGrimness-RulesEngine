using FDG.Ai;
using FDG.Ai.Tactician.Search;
using FDG.Players;
using FDG.Simulation;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #191 B4 (campaign step 8): the UCT loop over B2's tree - PUCT selection on the acting side's
    /// own component, progressive widening, root parallelism as a determinization ensemble, and the
    /// determinism guarantee (exact under a fixed seed, worker count and ITERATION budget; a time
    /// budget is deliberately not reproducible and no test uses one).
    /// <para>
    /// Authored trees carry SEED-DEPENDENT leaf values, so "different workers explore different
    /// determinizations" is a real assertion here rather than a vacuous one.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UctSearchTests
    {
        private static readonly PlayerID P0 = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        private static readonly PlayerID P1 = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        private static readonly PlayerID P2 = new(Guid.Parse("00000000-0000-0000-0000-000000000003"));

        // --- authored plumbing ------------------------------------------------------------------

        private sealed record AuthoredEdge(string Label, float Prior, string? ChildKey, bool Honored = true);

        private sealed record AuthoredUnit(string Name, float Prior, List<AuthoredEdge> Edges);

        /// <summary>
        /// An action space + expander over a fixed graph. A child's leaf value depends on the
        /// SIMULATION SEED (that is what a determinization is), so two workers genuinely see
        /// different samples of the same edge.
        /// </summary>
        private sealed class AuthoredGame : IActionSpace, INodeExpander
        {
            public readonly Dictionary<string, List<AuthoredUnit>> Units = new();
            public readonly Dictionary<string, PlayerID> ActingPlayerOf = new();
            public readonly Dictionary<string, float> BaseValue = new();
            public readonly List<int> SeedsUsed = new();
            public float SeedSpread = 0.25f;
            private readonly SideMap _sides;
            private readonly object _lock = new();

            public AuthoredGame(SideMap sides) => _sides = sides;

            public IReadOnlyList<UnitBranch> EnumerateUnits(SearchNode node) =>
                !Units.TryGetValue(node.Snapshot!, out List<AuthoredUnit>? units)
                    ? Array.Empty<UnitBranch>()
                    : units.Select((u, i) => new UnitBranch(i, default, u.Name, u.Prior)).ToList();

            public IReadOnlyList<SearchEdge> EnumerateEdges(SearchNode node, UnitBranch unit) =>
                Units[node.Snapshot!][unit.Index].Edges
                    .Select((e, i) => new SearchEdge(i, new SimulationService.Prescription(null), e.Prior, e.Label))
                    .ToList();

            public Task<ExpansionOutcome> Expand(SearchNode parent, SearchEdge edge, int seed)
            {
                lock (_lock) SeedsUsed.Add(seed);
                AuthoredEdge authored = Units[parent.Snapshot!]
                    .SelectMany(u => u.Edges).First(e => e.Label == edge.Label);
                if (!authored.Honored)
                    return Task.FromResult(new ExpansionOutcome(null, null, null, null, false, "authored fall-through"));

                // The determinization: a deterministic function of the seed, in [-spread, +spread].
                float jitter = ((seed % 1000) / 999f - 0.5f) * 2f * SeedSpread;
                float v0 = Math.Clamp(BaseValue[authored.ChildKey!] + jitter, 0f, 1f);
                var values = new SideValues(_sides.Count);
                for (int side = 0; side < _sides.Count; side++) values[side] = side == 0 ? v0 : (1f - v0) / (_sides.Count - 1);
                return Task.FromResult(new ExpansionOutcome(authored.ChildKey,
                    ActingPlayerOf[authored.ChildKey!], null, values, true, "ok"));
            }
        }

        /// <summary>Root (side 0) -> two units x two edges -> replies (side 1), each with two leaves.</summary>
        private static AuthoredGame TwoPly(out SideMap sides)
        {
            sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1) });
            var game = new AuthoredGame(sides);
            game.ActingPlayerOf["root"] = P0;
            game.Units["root"] = new()
            {
                new AuthoredUnit("u1", 0.6f, new()
                {
                    new AuthoredEdge("A", 0.55f, "A"),
                    new AuthoredEdge("B", 0.45f, "B"),
                }),
                new AuthoredUnit("u2", 0.4f, new()
                {
                    new AuthoredEdge("C", 0.60f, "C"),
                    new AuthoredEdge("D", 0.40f, "D"),
                }),
            };
            var baseValues = new Dictionary<string, float> { ["A"] = 0.75f, ["B"] = 0.45f, ["C"] = 0.55f, ["D"] = 0.35f };
            foreach ((string reply, float value) in baseValues)
            {
                game.BaseValue[reply] = value;
                game.ActingPlayerOf[reply] = P1;
                game.Units[reply] = new()
                {
                    new AuthoredUnit("opp", 1f, new()
                    {
                        new AuthoredEdge(reply + "1", 0.5f, reply + "1"),
                        new AuthoredEdge(reply + "2", 0.5f, reply + "2"),
                    }),
                };
                game.BaseValue[reply + "1"] = value + 0.10f;
                game.BaseValue[reply + "2"] = value - 0.10f;
                game.ActingPlayerOf[reply + "1"] = P0;
                game.ActingPlayerOf[reply + "2"] = P0;
            }
            return game;
        }

        private static Func<int, Task<SearchTree>> Factory(AuthoredGame game, SideMap sides,
            string rootKey = "root", SearchOptions? template = null) =>
            workerSeed =>
            {
                SearchOptions options = (template ?? new SearchOptions()) with { WorkerSeed = workerSeed };
                PlayerID acting = game.ActingPlayerOf[rootKey];
                var root = new SearchNode(rootKey, acting, sides.SideOf(acting), null,
                    SideValues.Uniform(sides.Count, 0.5f), 0, null, null);
                return Task.FromResult(new SearchTree(root, sides, options, game, game));
            };

        private static string Distribution(SearchResult result) =>
            string.Join("|", result.Root.Select(s => $"{s.UnitIndex}.{s.EdgeIndex}:{s.Visits}:{s.Value:F4}"));

        // --- (1) determinism ----------------------------------------------------------------------

        [Test]
        public async Task Search_IsExactlyReproducible_UnderFixedSeedWorkersAndIterations(
            [Values(1, 4)] int workers)
        {
            var options = new UctOptions { RootSeed = 7, Workers = workers, Iterations = 25 };

            AuthoredGame first = TwoPly(out SideMap sidesA);
            SearchResult a = await UctSearch.RunAsync(Factory(first, sidesA), options);
            AuthoredGame second = TwoPly(out SideMap sidesB);
            SearchResult b = await UctSearch.RunAsync(Factory(second, sidesB), options);

            Assert.That(a.Deterministic, Is.True, "an iteration budget is the reproducible mode");
            Assert.That(b.Choice!.Label, Is.EqualTo(a.Choice!.Label), "same seed and worker count -> same choice");
            Assert.That(Distribution(b), Is.EqualTo(Distribution(a)), "identical visit distribution and values");
            Assert.That(a.Iterations, Is.EqualTo(25 * workers), "every worker runs the full iteration budget");
            // Thread scheduling must not leak in: the SET of simulation seeds is identical too.
            Assert.That(second.SeedsUsed.OrderBy(s => s), Is.EqualTo(first.SeedsUsed.OrderBy(s => s)));
        }

        [Test]
        public async Task Workers_UseDistinctDeterminizations_AndSumTheirVisits()
        {
            AuthoredGame single = TwoPly(out SideMap sidesA);
            SearchResult one = await UctSearch.RunAsync(Factory(single, sidesA),
                new UctOptions { RootSeed = 11, Workers = 1, Iterations = 20 });

            AuthoredGame many = TwoPly(out SideMap sidesB);
            SearchResult four = await UctSearch.RunAsync(Factory(many, sidesB),
                new UctOptions { RootSeed = 11, Workers = 4, Iterations = 20 });

            Assert.That(four.Root.Sum(s => s.Visits), Is.GreaterThan(one.Root.Sum(s => s.Visits)),
                "four independent trees contribute four times the root visits");
            Assert.That(four.Workers, Is.EqualTo(4));

            // Design sec 6: two workers never share a determinization. Worker seeds are derived from
            // the root seed by worker index, so the same (depth, unit, edge) gets a different
            // simulation seed in each tree.
            var seeds = new HashSet<int>();
            for (int worker = 0; worker < 4; worker++)
            {
                int workerSeed = SearchSeeds.Derive(11, 0, 0, worker);
                Assert.That(seeds.Add(SearchSeeds.Derive(workerSeed, 0, 0, 0)), Is.True,
                    "each worker's first root edge draws its own determinization");
            }
        }

        // --- (2) PUCT ----------------------------------------------------------------------------

        [Test]
        public void Puct_ExploresByPrior_WhenUnvisited_AndExploitsValue_WhenVisited()
        {
            SideMap sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1) });
            var root = new SearchNode("root", P0, 0, null, SideValues.Uniform(2, 0.5f), 0, null, null);
            var low = new SearchEdge(0, new SimulationService.Prescription(null), 0.2f, "low-prior");
            var high = new SearchEdge(1, new SimulationService.Prescription(null), 0.8f, "high-prior");
            SearchNode Child(float v0) => new("c", P1, 1, null, new SideValues(v0, 1f - v0), 1, root, null);
            low.Child = Child(0.5f);
            high.Child = Child(0.5f);
            root.Units = new List<UnitBranch> { new(0, default, "u", 1f) { Edges = new() { low, high } } };

            // Unvisited children: the prior term decides.
            Assert.That(UctSearch.SelectPuct(root, 1.4f)!.Label, Is.EqualTo("high-prior"));

            // Give the low-prior edge visits and a much better value for the acting side.
            SearchTree.Backup(low.Child!, new SideValues(0.95f, 0.05f));
            SearchTree.Backup(low.Child!, new SideValues(0.95f, 0.05f));
            SearchTree.Backup(high.Child!, new SideValues(0.05f, 0.95f));
            Assert.That(UctSearch.SelectPuct(root, 0.1f)!.Label, Is.EqualTo("low-prior"),
                "with exploration small, the acting side's own Q decides");

            // And the exploration weight can still pull the other way.
            Assert.That(UctSearch.SelectPuct(root, 50f)!.Label, Is.EqualTo("high-prior"),
                "with exploration large, the prior dominates again");
        }

        [Test]
        public void Puct_ReadsTheActingSidesOwnComponent_NotTheRoots()
        {
            SideMap sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1), (P2, 2) });
            // A node acting for side 2 in a three-sided game.
            var node = new SearchNode("n", P2, 2, null, SideValues.Uniform(3, 0.33f), 1, null, null);
            var goodForSide0 = new SearchEdge(0, new SimulationService.Prescription(null), 0.5f, "good-for-0");
            var goodForSide2 = new SearchEdge(1, new SimulationService.Prescription(null), 0.5f, "good-for-2");
            goodForSide0.Child = new SearchNode("a", P0, 0, null, new SideValues(0.9f, 0.05f, 0.05f), 2, node, null);
            goodForSide2.Child = new SearchNode("b", P0, 0, null, new SideValues(0.05f, 0.05f, 0.9f), 2, node, null);
            SearchTree.Backup(goodForSide0.Child!, goodForSide0.Child!.LeafValues);
            SearchTree.Backup(goodForSide2.Child!, goodForSide2.Child!.LeafValues);
            node.Units = new List<UnitBranch> { new(0, default, "u", 1f) { Edges = new() { goodForSide0, goodForSide2 } } };

            Assert.That(UctSearch.SelectPuct(node, 0.01f)!.Label, Is.EqualTo("good-for-2"),
                "max^n: a side maximizes ITS OWN value, it does not minimize anyone else's");
        }

        // --- (3) budget ---------------------------------------------------------------------------

        [Test]
        public void Budget_ScalesWithRootBranching_AndIsCapped()
        {
            var options = new UctOptions { BaseBudgetMs = 1000, BudgetMsPerRootUnit = 120, MaxBudgetMs = 2000 };
            Assert.That(options.BudgetMsFor(0), Is.EqualTo(1000), "floor");
            Assert.That(options.BudgetMsFor(4), Is.EqualTo(1480), "a 2k-sized root gets more than the floor");
            Assert.That(options.BudgetMsFor(9), Is.GreaterThan(options.BudgetMsFor(4)),
                "a 4k-sized root gets more time, not a shallower tree");
            Assert.That(options.BudgetMsFor(40), Is.EqualTo(2000), "hard cap");
            Assert.That(UctOptions.Benchmark.MaxBudgetMs, Is.EqualTo(2000), "plan sec 9: 1-2s in benchmarks");
            Assert.That(UctOptions.Interactive.BaseBudgetMs, Is.EqualTo(5000), "plan sec 9: 5-10s vs humans");
            Assert.That(UctOptions.Interactive.MaxBudgetMs, Is.EqualTo(10_000));
        }

        // --- (4) closed edges ----------------------------------------------------------------------

        [Test]
        public async Task ClosedEdges_AreNeverChosen_AndTheNextInPriorOrderTakesTheSlot()
        {
            SideMap sides = SideMap.FromSlots(new[] { (P0, 0), (P1, 1) });
            var game = new AuthoredGame(sides);
            game.ActingPlayerOf["root"] = P0;
            game.Units["root"] = new()
            {
                new AuthoredUnit("u1", 1f, new()
                {
                    new AuthoredEdge("top", 0.7f, null, Honored: false),   // falls through at play
                    new AuthoredEdge("second", 0.3f, "S"),
                }),
            };
            game.BaseValue["S"] = 0.6f;
            game.ActingPlayerOf["S"] = P1;
            game.Units["S"] = new();
            game.SeedSpread = 0f;

            SearchResult result = await UctSearch.RunAsync(Factory(game, sides),
                new UctOptions { RootSeed = 3, Workers = 1, Iterations = 6 });

            Assert.That(result.Choice!.Label, Is.EqualTo("second"),
                "an unhonored edge is closed and never credited, so the next prior takes the choice");
            Assert.That(result.Root.Any(s => s.Label == "top"), Is.False, "a closed edge has no statistics at all");
            Assert.That(result.ClosedEdges, Is.EqualTo(1));
        }

        // --- (5) real engine: end-to-end determinism, and the in-sim policy question ----------------

        [Test]
        [CancelAfter(300_000)]
        public async Task Search_OnARealBoundary_IsReproducible_AndPlaysAnHonoredEdge()
        {
            string snapshot = TacticianActionSpaceTests.Fixture.Snapshot(4, objectives: 3);
            var options = new UctOptions
            {
                RootSeed = 99,
                Workers = 2,
                Iterations = 3,
                Tree = new SearchOptions { InSimProfile = EAiProfile.Tactician, TimeoutSeconds = 120 },
            };

            SearchResult first = await UctSearch.RunAsync(snapshot, options, new HandWeightedEvaluator());
            SearchResult second = await UctSearch.RunAsync(snapshot, options, new HandWeightedEvaluator());

            Assert.That(first.Choice, Is.Not.Null, "the search returns a prescription to play");
            Assert.That(first.Choice!.Prescription.Unit, Is.Not.Null, "a composite edge names its unit");
            Assert.That(second.Choice!.Label, Is.EqualTo(first.Choice.Label), "reproducible on a real board");
            Assert.That(Distribution(second), Is.EqualTo(Distribution(first)),
                "identical visit distribution across two runs of the same seed");
            Assert.That(first.Nodes, Is.GreaterThan(2), "the tree actually grew");
        }

        /// <summary>
        /// Resolves the design doc's deferred "in-sim policy: Tactician vs SoloRules" (sec 9). It is
        /// not a free cost/bias trade: a prescription is consumed BY THE PLANNER (5b's seam), and a
        /// non-planning profile has no planner - so under SoloRules every edge falls through and
        /// closes, and the search has no tree at all. Pinned so nobody re-opens the question by
        /// flipping the option and finding a silently empty search.
        /// </summary>
        [Test]
        [CancelAfter(300_000)]
        public async Task InSimSoloRules_ClosesEveryEdge_SoTheInSimPolicyMustPlan()
        {
            string snapshot = TacticianActionSpaceTests.Fixture.Snapshot(4, objectives: 3);
            SearchResult result = await UctSearch.RunAsync(snapshot, new UctOptions
            {
                RootSeed = 5,
                Workers = 1,
                Iterations = 3,
                Tree = new SearchOptions { InSimProfile = EAiProfile.SoloRules, TimeoutSeconds = 120 },
            }, new HandWeightedEvaluator());

            Assert.That(result.Choice, Is.Null, "no edge can be opened under a non-planning in-sim policy");
            Assert.That(result.ClosedEdges, Is.GreaterThan(0), "every attempted edge closed as unhonored");
        }
    }
}
