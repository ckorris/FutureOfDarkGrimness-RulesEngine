using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.Simulation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #191 B2 (docs/tactician-b2-design.md sec 8) on real engine states: the two-level action space
    /// (tests 1-3), B reduces to A (test 4), determinism under the derived seed (test 6), and the
    /// evaluators' two-side constraint. The 1k/4k here are unit COUNTS on authored scenarios (4 and
    /// 16 units a side); the real-army counts are reported by <c>fdglab b0</c>'s B2 phase.
    /// </summary>
    [TestFixture]
    public class TacticianActionSpaceTests
    {
        private static SearchOptions Options(int workerSeed = 1, ERandomnessType randomness = ERandomnessType.Realistic) =>
            new()
            {
                WorkerSeed = workerSeed,
                Randomness = randomness,
                InSimProfile = EAiProfile.Tactician,
                TimeoutSeconds = 90,
            };

        // --- test 1: candidate counts ----------------------------------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task ActionSpace_UnitCountIsThePool_AndEdgeCountsRespectBudgetAndFamilies([Values(4, 16)] int unitsPerSide)
        {
            string start = Fixture.Snapshot(unitsPerSide);
            SearchOptions options = Options();
            SearchTree tree = await SearchTree.FromSnapshotAsync(start, options,
                new TacticianActionSpace(options), new TerminalOnlyEvaluator());

            IReadOnlyList<UnitBranch> units = tree.UnitsOf(tree.Root);
            Assert.That(units.Count, Is.EqualTo(unitsPerSide),
                "level 1 is exactly the acting player's unactivated living units");
            Assert.That(units.Select(u => u.Prior).Sum(), Is.EqualTo(1f).Within(1e-4f), "priors are a distribution");
            for (int i = 1; i < units.Count; i++)
                Assert.That(units[i].Prior, Is.LessThanOrEqualTo(units[i - 1].Prior), "prior order");

            GameDataStore store = GameSaveSerializer.Load(tree.Root.Snapshot!);
            var table = new TableState(store);
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            foreach (UnitBranch unit in units)
            {
                IReadOnlyList<SearchEdge> edges = tree.EdgesOf(tree.Root, unit);
                DataBinding<UnitData> binding = store.GetDataBinding<UnitData>(unit.Unit);
                List<MacroAction> candidates = MacroActionGenerator.Enumerate(evaluator, table, binding, options.CandidateBudget);
                int families = candidates.Select(c => c.Intent).Distinct().Count();

                Assert.That(edges.Count, Is.GreaterThanOrEqualTo(families),
                    $"{unit.Name}: every intent family the generator emits has an edge");
                // Budget: the generator's diversity rule may exceed the budget by at most one per
                // family (round 0 always completes); the Hold Shoot/Pass twin adds one more.
                Assert.That(edges.Count, Is.LessThanOrEqualTo(Math.Max(options.CandidateBudget, families) + 1),
                    $"{unit.Name}: edge count within the ranking budget");
                var intents = edges.Where(e => e.Prescription.Macro != null).Select(e => e.Prescription.Macro!.Intent).Distinct();
                Assert.That(intents, Is.EquivalentTo(candidates.Select(c => c.Intent).Distinct()),
                    $"{unit.Name}: the edge set carries exactly the generator's families");
                for (int i = 1; i < edges.Count; i++)
                    Assert.That(edges[i].Prior, Is.LessThanOrEqualTo(edges[i - 1].Prior + 1e-6f), "prior order");
            }
        }

        // --- test 2: the diversity rule survives -----------------------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task ActionSpace_KeepsAChargeCandidateAsAnEdgeEvenWhenItScoresLast()
        {
            // Enemies within charge reach, so the generator emits a Reachable ChargeToContact for
            // rifle-armed infantry that the scorer dislikes (they would rather stand and shoot).
            string start = Fixture.Snapshot(unitsPerSide: 3, gapInches: 8f);
            SearchOptions options = Options();
            SearchTree tree = await SearchTree.FromSnapshotAsync(start, options,
                new TacticianActionSpace(options), new TerminalOnlyEvaluator());

            bool anyCharge = false;
            foreach (UnitBranch unit in tree.UnitsOf(tree.Root))
            {
                IReadOnlyList<SearchEdge> edges = tree.EdgesOf(tree.Root, unit);
                SearchEdge? charge = edges.FirstOrDefault(e => e.Prescription.Action == ChooseActionStage.CHARGE_CHOICE_NAME);
                if (charge == null) continue;
                anyCharge = true;
                Assert.That(charge.Prescription.Macro!.Intent, Is.EqualTo(EMacroIntent.ChargeToContact));
                Assert.That(charge.Prescription.Macro.Feasibility, Is.EqualTo(EFeasibility.Reachable));
                // It is an edge whether or not it ranks first: priors ORDER, they never drop.
                Assert.That(edges.ToList().IndexOf(charge), Is.GreaterThanOrEqualTo(0));
            }
            Assert.That(anyCharge, Is.True, "fixture must produce a reachable charge to pin the diversity rule on");
        }

        // --- test 3: honored / closed at play --------------------------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task OpeningAnEdgeTheStageDoesNotOffer_ClosesItInsteadOfCreditingNaturalPlay()
        {
            string start = Fixture.Snapshot(3);
            SearchOptions options = Options();
            SearchTree tree = await SearchTree.FromSnapshotAsync(start, options,
                new TacticianActionSpace(options), new TerminalOnlyEvaluator());
            UnitBranch unit = tree.UnitsOf(tree.Root)[0];
            tree.EdgesOf(tree.Root, unit);

            // Nobody in this fixture is a caster, so Cast is never offered: 5b falls through to
            // natural scoring, and the honored flag must say so.
            var bogus = new SearchEdge(99, new SimulationService.Prescription(unit.Unit, ChooseActionStage.CAST_CHOICE_NAME),
                0.1f, "bogus cast");
            SearchNode? child = await tree.OpenAsync(tree.Root, unit, bogus);

            Assert.That(child, Is.Null);
            Assert.That(bogus.Closed, Is.True);
            Assert.That(bogus.ClosedReason, Does.Contain("fell through"));

            // And a real edge is honored and opens.
            SearchEdge real = unit.Edges![0];
            SearchNode? opened = await tree.OpenAsync(tree.Root, unit, real);
            Assert.That(opened, Is.Not.Null, real.ClosedReason);
            Assert.That(real.Closed, Is.False);
            Assert.That(opened!.Snapshot, Is.Not.Null);
            Assert.That(opened.Depth, Is.EqualTo(1));
        }

        // --- test 4: B reduces to A ------------------------------------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task OneExpansion_IsExactlyTheNaturalTacticianActivation_ByteForByte()
        {
            string start = Fixture.Snapshot(4);
            SearchOptions options = Options() with { WideningC = 1f, WideningAlpha = 0f };
            SearchTree tree = await SearchTree.FromSnapshotAsync(start, options,
                new TacticianActionSpace(options), new TerminalOnlyEvaluator());

            SearchNode leaf = await ExpansionScaffold.IterateAsync(tree);
            SearchEdge choice = tree.RootChoice()!;
            Assert.That(choice.Child, Is.SameAs(leaf));
            Assert.That(tree.Root.Units!.Count(u => u.Edges != null), Is.EqualTo(1), "one expansion opened one unit");
            Assert.That(choice, Is.SameAs(tree.Root.Units![0].Edges![0]), "...and its first edge: A's own move");

            // Natural play from the same root under the same seed the edge was simulated with.
            int seed = SearchSeeds.Derive(options.WorkerSeed, 0, 0, 0);
            SimulationService.SimulationResult natural = await new SimulationService(new SimulationService.SimulationOptions
            {
                Profile = EAiProfile.Tactician,
                Seed = seed,
                Randomness = options.Randomness,
                TimeoutSeconds = 90,
            }).Advance(tree.Root.Snapshot!, null);

            Assert.That(natural.ReachedEndOfLine, Is.True, natural.Note);
            Assert.That(leaf.Snapshot, Is.EqualTo(natural.Snapshot),
                "a tree allowed one expansion plays exactly A's move - prescribing the top edge must " +
                "reproduce natural Tactician play byte for byte");
            Assert.That(leaf.ActingPlayer, Is.EqualTo(natural.ActingPlayerAtEnd!.Value));
        }

        // --- test 6: determinism under the derived seed ------------------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task ExpandingTheSameEdge_UnderTheSameSeed_IsByteIdentical_AndAnotherWorkerDiffers()
        {
            string start = Fixture.Snapshot(3);
            SearchOptions options = Options(workerSeed: 5);
            SearchTree tree = await SearchTree.FromSnapshotAsync(start, options,
                new TacticianActionSpace(options), new TerminalOnlyEvaluator());
            UnitBranch unit = tree.UnitsOf(tree.Root)[0];
            SearchEdge edge = tree.EdgesOf(tree.Root, unit)[0];

            var expander = new SimulationExpander(options, new TerminalOnlyEvaluator(), tree.Sides);
            int seed = SearchSeeds.Derive(options.WorkerSeed, 0, unit.Index, edge.Index);
            ExpansionOutcome first = await expander.Expand(tree.Root, edge, seed);
            ExpansionOutcome second = await expander.Expand(tree.Root, edge, seed);
            Assert.That(first.Succeeded, Is.True, first.Note);
            Assert.That(second.Snapshot, Is.EqualTo(first.Snapshot), "same derived seed, same child, byte for byte");

            int otherWorker = SearchSeeds.Derive(options.WorkerSeed + 1, 0, unit.Index, edge.Index);
            Assert.That(otherWorker, Is.Not.EqualTo(seed));
            // A different worker rolls different dice. The first unit's activation in this fixture
            // shoots (rifles at 20"), so the realistic rolls land differently.
            ExpansionOutcome other = await expander.Expand(tree.Root, edge, otherWorker);
            Assert.That(other.Succeeded, Is.True, other.Note);
            Assert.That(other.Snapshot, Is.Not.EqualTo(first.Snapshot),
                "another worker's seed must give another determinization of the same edge");
        }

        // --- evaluators: the two-side constraint ------------------------------------------------------

        [Test]
        [CancelAfter(120_000)]
        public void ShippedEvaluators_SatisfyTheTwoSideConstraint()
        {
            GameDataStore store = GameSaveSerializer.Load(Fixture.Snapshot(3, objectives: 3));
            SideMap sides = SideMap.FromStore(store);
            var state = new TableState(store);

            foreach (IPositionEvaluator evaluator in new IPositionEvaluator[] { new TerminalOnlyEvaluator(), new ObjectiveShareEvaluator() })
            {
                SideValues values = evaluator.Evaluate(state, sides);
                Assert.That(values.Count, Is.EqualTo(2));
                Assert.That(values.IsComplementaryTwoSide(), Is.True, $"{evaluator.GetType().Name}: {values}");
                Assert.That(values[0], Is.InRange(0f, 1f));
            }

            // Objective share reads the projection: reds sit on two of the three markers here.
            SideValues share = new ObjectiveShareEvaluator().Evaluate(state, sides);
            Assert.That(share[0], Is.GreaterThan(0.5f), $"reds hold more markers: {share}");
        }

        // --- fixture -----------------------------------------------------------------------------

        private static class Fixture
        {
            public static string Snapshot(int unitsPerSide, float gapInches = 20f, int objectives = 0)
            {
                GameDataStore store = ScenarioCompiler.Compile(Scenario(unitsPerSide, gapInches, objectives),
                    new[] { Army("Reds", unitsPerSide), Army("Blues", unitsPerSide) });
                return SimulationService.Snapshot(store);
            }

            private static ScenarioFile Scenario(int unitsPerSide, float gapInches, int objectives)
            {
                var file = new ScenarioFile
                {
                    Name = "B2 action space fixture",
                    Round = 2,
                    ActivePlayer = 0,
                    Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = 4242 },
                    Players = new() { Player("reds.fdgarmy", 20f, unitsPerSide), Player("blues.fdgarmy", 20f + gapInches, unitsPerSide) },
                };
                if (objectives > 0)
                {
                    // Two on the reds' side of the table, one on the blues'.
                    file.Objectives = new() { new[] { 21f, 8f }, new[] { 21f, 14f }, new[] { 20f + gapInches + 1f, 8f } };
                }
                return file;
            }

            private static ScenarioPlayer Player(string army, float x, int units)
            {
                var player = new ScenarioPlayer { Army = army, Units = new() };
                for (int i = 0; i < units; i++)
                {
                    float z = 6f + i * 3f;
                    player.Units.Add(new ScenarioUnit
                    {
                        UnitIndex = i,
                        Models = new() { new[] { x, z }, new[] { x + 1f, z }, new[] { x + 2f, z } },
                    });
                }
                return player;
            }

            private static ArmyListFile Army(string name, int units)
            {
                var army = new ArmyListFile { Name = name, Units = new() };
                for (int i = 0; i < units; i++)
                {
                    army.Units.Add(new UnitFileEntry
                    {
                        Name = $"{name} Squad {i + 1}", ModelCount = 3, Quality = 4, Defense = 4,
                        Weapons = new()
                        {
                            new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 },
                            new WeaponFileEntry { Name = "CCW", RangeInches = 0, Attacks = 1 },
                        },
                    });
                }
                return army;
            }
        }
    }
}
