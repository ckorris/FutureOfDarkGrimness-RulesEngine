using FDG.Ai;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.Simulation;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 B5 (campaign step 9) - the rung where search starts driving a real game.
    //
    // Everything above this point was measured in the lab: B2 built the tree, B3 valued the leaves,
    // B4 searched them. None of it touched a game. These tests pin the two halves of the contract
    // that makes it a BOT rather than a benchmark:
    //   (a) the search's choice actually reaches the game, THROUGH the policy, and is honored;
    //   (b) when the search cannot run, the game continues on the A policy - G3, counted, silent to
    //       the player. That half matters more than the first: a search is a whole resumed engine
    //       and can fault, and a real game has nowhere to put a fault.
    [TestFixture]
    public class StrategistIntegrationTests
    {
        // Small but real: a fixed iteration budget (never a time budget - G5) so the test is
        // reproducible and finishes, with the in-sim policy the design settled on.
        private static UctOptions Budget => new()
        {
            RootSeed = 7,
            Workers = 1,
            Iterations = 2,
            Tree = new SearchOptions { InSimProfile = EAiProfile.Tactician, TimeoutSeconds = 120 },
        };

        [Test]
        [CancelAfter(300_000)]
        public async Task Strategist_PrescribesTheSearchsChoice_ThroughThePolicy_AndItIsHonored()
        {
            // Stand exactly where the resolver stands in a real game: at the boundary the engine
            // itself stops at, with that boundary's own re-saved store as the live position.
            (GameDataStore store, PlayerID acting) = await BoundaryAsync();
            var tableState = new TableState(store);
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            var planner = new TacticianPlanner(tableState, evaluator);
            var policy = new TacticianActivationResolver(tableState, evaluator, planner);
            var resolver = new StrategistActivationResolver(tableState, planner, policy,
                new HandWeightedEvaluator(), Budget);

            ChooseUnitToActivateRequest request = Request(store, tableState, acting);
            DataBinding<UnitData> chosen = await resolver.Resolve(request);

            Assert.That(resolver.Searches, Is.EqualTo(1), "one activation, one search");
            Assert.That(resolver.Fallbacks, Is.EqualTo(0),
                "the search ran on a real boundary and must have produced a choice");
            Assert.That(request.ValidOptions.Any(o => o.Option.Reference.Equals(chosen.Reference)),
                "the answer is always one of the options the stage offered");
            Assert.That(resolver.LastPrescription, Is.Not.Null, "the search prescribed something");
            Assert.That(resolver.LastPrescription!.Unit, Is.EqualTo(chosen.Reference),
                "the unit the game activated IS the unit the search chose - the seam actually steers");
            Assert.That(resolver.LastPrescription.Action, Is.Not.Null,
                "the edge carries the action too, for Choose Action to consume later in this activation");
            Assert.That(planner.ActiveUnit, Is.Not.Null,
                "BeginActivation still ran, so the rest of the activation has its planner state (B0 finding 4)");
            Assert.That(planner.ActiveUnit!.Reference, Is.EqualTo(chosen.Reference),
                "and it ran on the prescribed unit, not on whatever urgency would have picked");
            // Deliberately NOT asserting LastPrescriptionHonored here: it is the AND of both halves,
            // and the action half is only settled once Choose Action runs later in this activation.
        }

        [Test]
        [CancelAfter(300_000)]
        public async Task Strategist_WhenTheSearchCannotRun_FallsBackToTheAPolicy_AndCountsIt()
        {
            // A store that is not a resumable game: the root probe finds no activation boundary, so
            // the search has nothing to root a tree on. The game must not notice.
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            var planner = new TacticianPlanner(tableState, evaluator);
            var us = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> unit = MakeUnit(store, us);
            var policy = new TacticianActivationResolver(tableState, evaluator, planner);
            var resolver = new StrategistActivationResolver(tableState, planner, policy,
                new HandWeightedEvaluator(), Budget);

            var request = new ChooseUnitToActivateRequest(us,
                new List<SelectionRequest<UnitData>.ValidOption>
                {
                    new(unit, unit.GetValue().Name),
                },
                new List<SelectionRequest<UnitData>.InvalidOption>());

            DataBinding<UnitData> chosen = await resolver.Resolve(request);

            Assert.That(chosen.Reference, Is.EqualTo(unit.Reference),
                "the A policy answered, so the game continues normally");
            Assert.That(resolver.Searches, Is.EqualTo(1));
            Assert.That(resolver.Fallbacks, Is.EqualTo(1), "G3 fallbacks are counted, not swallowed");
            Assert.That(planner.HasPrescription, Is.False,
                "a failed search leaves NO prescription behind to poison the next activation");
        }

        [Test]
        public void SearchNeverRunsInsideASimulation_SoTheProfileDegradesToTheAPolicy()
        {
            // A Strategist in-sim policy would root a new tree at every boundary of every line of
            // the tree above it. The guard lives in SimulationService; this pins the intent so the
            // mapping is not quietly removed.
            Assert.That(AiProfileFactory.DefaultSearchBudget.Tree.InSimProfile,
                Is.EqualTo(EAiProfile.Tactician),
                "the opponent model inside the search is the A policy, and the search says so");
        }

        [Test]
        public void TheStrategistRegistryIsTheTacticiansPlusSearch()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            var playerID = new PlayerID(Guid.NewGuid());

            AiProfileFactory.BuildRegistry(EAiProfile.Strategist, tableState, playerID,
                out TacticianPlanner? planner, seed: 1);

            Assert.That(planner, Is.Not.Null,
                "the Strategist drives the same planner the Tactician does - that is the whole seam");
        }

        // --- helpers -------------------------------------------------------------------------------

        /// <summary>
        /// The engine's own first activation boundary from the B2 fixture, and its re-saved store:
        /// the exact position a resolver is called at.
        /// </summary>
        private static async Task<(GameDataStore Store, PlayerID Acting)> BoundaryAsync()
        {
            string snapshot = TacticianActionSpaceTests.Fixture.Snapshot(4, objectives: 3);
            var service = new SimulationService(new SimulationService.SimulationOptions
            {
                Profile = EAiProfile.Tactician,
                Seed = 11,
                TimeoutSeconds = 120,
            });
            SimulationService.SimulationResult probe = await service.Probe(snapshot);
            Assert.That(probe.ActingPlayerAtEnd, Is.Not.Null, "fixture must resume to a real boundary");
            return (GameSaveSerializer.Load(probe.Snapshot!), probe.ActingPlayerAtEnd!.Value);
        }

        private static ChooseUnitToActivateRequest Request(GameDataStore store, ITableState tableState,
            PlayerID acting)
        {
            List<SelectionRequest<UnitData>.ValidOption> options = store.GetAllValues<ArmyData>()
                .Where(army => army.IsOwnedBy(acting))
                .SelectMany(army => army.UnitBindings)
                .Where(binding => binding.GetValue().GetIsAlive() && binding.GetValue().GetIsOnBattlefield())
                .Select(binding => new SelectionRequest<UnitData>.ValidOption(binding, binding.GetValue().Name))
                .ToList();
            Assert.That(options, Is.Not.Empty, "the acting player must have something to activate");
            return new ChooseUnitToActivateRequest(acting, options,
                new List<SelectionRequest<UnitData>.InvalidOption>());
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID owner)
        {
            var model = new ModelData(baseRadiusInches: 0.5f,
                weapons: new List<Weapon> { new("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0) },
                initialPosition: new Position(20f, 24f), gameDataStore: store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            };
            var unit = new UnitData(owner, "Lone Unit", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = store.GetDataBinding<UnitData>(store.Create(unit));
            store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
