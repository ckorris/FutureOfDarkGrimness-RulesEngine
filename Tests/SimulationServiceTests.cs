using FDG.Ai;
using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Simulation;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #191 B1 step 5c: the pause/step hook, the bus bypass, and <see cref="SimulationService"/>'s
    /// line API, pinned on authored states.
    /// <para>
    /// These are the ENGINE-side pins - shape, determinism, depth, steering, and the equivalence of
    /// the bypassed request path with the bus one. The per-activation COST at real 2k/4k army sizes
    /// is measured lab-side with <c>fdglab b0</c> against B0's table; an authored three-unit state
    /// is the right place to pin behavior and the wrong place to measure performance.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SimulationServiceTests
    {
        [Test]
        [CancelAfter(120_000)]
        public async Task Run_PlaysALineAndReturnsTheSnapshotAtTheNextBoundary()
        {
            string start = AuthoredSnapshot();

            SimulationService.SimulationResult result =
                await Sim(seed: 4242).RunNatural(start, activations: 2);

            Assert.That(result.ReachedEndOfLine, Is.True, result.Note);
            Assert.That(result.ActivationsRun, Is.EqualTo(2));
            Assert.That(result.Snapshot, Is.Not.EqualTo(start),
                "two activations must move the game on from the snapshot it started at.");
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task Run_IsDeterministicUnderAFixedSeed()
        {
            string start = AuthoredSnapshot();

            SimulationService.SimulationResult first = await Sim(seed: 7001).RunNatural(start, 3);
            SimulationService.SimulationResult second = await Sim(seed: 7001).RunNatural(start, 3);

            Assert.That(first.ReachedEndOfLine, Is.True, first.Note);
            Assert.That(second.Snapshot, Is.EqualTo(first.Snapshot),
                "same snapshot, same seed, same line must reproduce byte for byte - B4's search " +
                "cannot be seeded otherwise (G5).");
        }

        // Depth is a parameter from day one (campaign step 5): a longer line must genuinely play
        // further, not silently stop at one activation.
        [Test]
        [CancelAfter(120_000)]
        public async Task Run_LineLengthIsTheSearchDepth()
        {
            string start = AuthoredSnapshot();

            SimulationService.SimulationResult shallow = await Sim(seed: 11).RunNatural(start, 1);
            SimulationService.SimulationResult deep = await Sim(seed: 11).RunNatural(start, 4);

            Assert.That(shallow.ActivationsRun, Is.EqualTo(1));
            Assert.That(deep.ActivationsRun, Is.EqualTo(4));
            Assert.That(deep.Snapshot, Is.Not.EqualTo(shallow.Snapshot),
                "a four-activation line must reach a different state than a one-activation line.");
        }

        // The 5c bypass is the one change here that touches a path real play also uses the shape of,
        // so it gets its own falsifiable pin: the SAME line, run both ways, must agree exactly.
        // (Hash-verify cannot cover this - real games keep the bus, so they never exercise it.)
        [Test]
        [CancelAfter(180_000)]
        public async Task BusBypass_ProducesTheSameGameAsTheBusPath()
        {
            string start = AuthoredSnapshot();

            SimulationService.SimulationResult bypassed =
                await Sim(seed: 909, bypassBus: true).RunNatural(start, 3);
            SimulationService.SimulationResult throughBus =
                await Sim(seed: 909, bypassBus: false).RunNatural(start, 3);

            Assert.That(bypassed.ReachedEndOfLine, Is.True, bypassed.Note);
            Assert.That(throughBus.ReachedEndOfLine, Is.True, throughBus.Note);
            Assert.That(bypassed.Snapshot, Is.EqualTo(throughBus.Snapshot),
                "answering decisions directly from the registry must be indistinguishable from " +
                "answering them over the bus - the bypass is a cost removal, not a behavior change.");
        }

        // The hook and 5b's prescription seam have to compose: a prescribed unit must actually be
        // the one that activates, which shows up as a different resulting state.
        [Test]
        [CancelAfter(180_000)]
        public async Task Prescription_SteersWhichUnitActivates()
        {
            string start = AuthoredSnapshot();
            IReadOnlyList<DataReference> units = LivingUnitsOfPlayer(start, slotID: 0);
            Assert.That(units.Count, Is.GreaterThanOrEqualTo(2),
                "this pin needs a player with at least two units to choose between.");

            SimulationService.SimulationResult first = await Sim(seed: 31).Advance(
                start, new SimulationService.Prescription(units[0]));
            SimulationService.SimulationResult second = await Sim(seed: 31).Advance(
                start, new SimulationService.Prescription(units[^1]));

            Assert.That(first.ReachedEndOfLine, Is.True, first.Note);
            Assert.That(second.ReachedEndOfLine, Is.True, second.Note);
            Assert.That(first.Snapshot, Is.Not.EqualTo(second.Snapshot),
                "prescribing a different unit must produce a different game - otherwise the tree " +
                "edge is not steering anything.");
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task Run_ReportsANaturalGameEndInsteadOfFaulting()
        {
            // A line longer than the game has activations left must come back with the game's real
            // result (a terminal node for search), not a fault and not a hang.
            string start = AuthoredSnapshot(round: GameWideConstants.NUMBER_OF_ROUNDS);

            SimulationService.SimulationResult result = await Sim(seed: 5).RunNatural(start, 400);

            Assert.That(result.ReachedEndOfLine, Is.False);
            Assert.That(result.EndedEarly, Is.Not.Null, result.Note);
            Assert.That(result.EndedEarly!.Outcome, Is.Not.EqualTo(EGameOutcome.Fault), result.Note);
        }

        // #191 B2 sec 4.4 / test 7: the callback line is the list line. Same seed, same
        // prescriptions handed out lazily, same stop - byte-identical snapshot, same honored flags.
        [Test]
        [CancelAfter(180_000)]
        public async Task CallbackLine_IsByteIdenticalToTheListLine()
        {
            string start = AuthoredSnapshot();
            IReadOnlyList<DataReference> units = LivingUnitsOfPlayer(start, slotID: 0);
            var prescriptions = new SimulationService.Prescription?[]
            {
                new SimulationService.Prescription(units[^1]), null, null,
            };

            SimulationService.SimulationResult list = await Sim(seed: 606).Run(start, prescriptions);
            SimulationService.SimulationResult callback = await Sim(seed: 606).Run(start,
                new RecordingDriver(prescriptions));

            Assert.That(list.ReachedEndOfLine, Is.True, list.Note);
            Assert.That(callback.Snapshot, Is.EqualTo(list.Snapshot));
            Assert.That(callback.Honored, Is.EqualTo(list.Honored));
            Assert.That(list.Honored, Is.EqualTo(new[] { true, true, true }), "one flag per activation, natural = honored");
            Assert.That(list.ActivationsRun, Is.EqualTo(3));
            Assert.That(callback.ActingPlayerAtEnd, Is.EqualTo(list.ActingPlayerAtEnd));
            Assert.That(list.ActingPlayerAtStart, Is.Not.Null);
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task Probe_ReportsTheActingPlayerWithoutPlayingAnything()
        {
            string start = AuthoredSnapshot();
            GameDataStore store = GameSaveSerializer.Load(start);
            PlayerID slot0 = store.GetAllValues<PlayerSlotInfo>().First(i => i.SlotID == 0).PlayerID;

            SimulationService.SimulationResult probe = await Sim(seed: 1).Probe(start);

            Assert.That(probe.ReachedEndOfLine, Is.True, probe.Note);
            Assert.That(probe.ActivationsRun, Is.EqualTo(0));
            Assert.That(probe.Honored, Is.Empty);
            Assert.That(probe.ActingPlayerAtEnd, Is.EqualTo(slot0), "the scenario's ActivePlayer is slot 0");
            // Probing the probe's own snapshot lands on the same boundary (the serialized text is not
            // byte-stable across re-saves - a store generation counter ticks - so the pin is the
            // acting player, which is what a search root reads).
            SimulationService.SimulationResult again = await Sim(seed: 1).Probe(probe.Snapshot!);
            Assert.That(again.ActingPlayerAtEnd, Is.EqualTo(probe.ActingPlayerAtEnd));
            Assert.That(again.ActivationsRun, Is.EqualTo(0));
        }

        // A prescription to a unit that is not activatable falls through (5b's G3 rule) - and the
        // line must SAY so, or search credits the edge with A's natural move (B2 sec 4.3).
        [Test]
        [CancelAfter(120_000)]
        public async Task StalePrescription_IsReportedAsNotHonored()
        {
            string start = AuthoredSnapshot();
            IReadOnlyList<DataReference> enemyUnits = LivingUnitsOfPlayer(start, slotID: 1);

            // Slot 0 acts first; prescribing one of slot 1's units to it can never be honored.
            SimulationService.SimulationResult result = await Sim(seed: 77).Run(start,
                new SimulationService.Prescription?[] { new SimulationService.Prescription(enemyUnits[0]), null });

            Assert.That(result.ReachedEndOfLine, Is.True, result.Note);
            Assert.That(result.Honored, Is.EqualTo(new[] { false, true }));
        }

        private sealed class RecordingDriver : SimulationService.ILineDriver
        {
            private readonly IReadOnlyList<SimulationService.Prescription?> _line;
            public RecordingDriver(IReadOnlyList<SimulationService.Prescription?> line) => _line = line;

            public SimulationService.LineStep AtBoundary(SimulationService.LineBoundary boundary)
            {
                Assert.That(boundary.State, Is.Not.Null, "the driver sees the live state at every boundary");
                if (boundary.Index > 0) Assert.That(boundary.PreviousHonored, Is.Not.Null);
                if (boundary.Index >= _line.Count) return SimulationService.LineStep.Stop;
                return _line[boundary.Index] is { } p
                    ? SimulationService.LineStep.Prescribe(p)
                    : SimulationService.LineStep.Natural;
            }
        }

        // --- helpers ---------------------------------------------------------------------------

        private static SimulationService Sim(int seed, bool bypassBus = true) =>
            new(new SimulationService.SimulationOptions
            {
                // Tactician, because it is the profile the prescription seam belongs to and the one
                // search will actually simulate under. Realistic dice (not the simulation default of
                // Probabilistic) so "same seed reproduces byte for byte" is a real claim about real
                // rolls rather than about expected-value arithmetic.
                Profile = EAiProfile.Tactician,
                Seed = seed,
                Randomness = ERandomnessType.Realistic,
                BypassBus = bypassBus,
                TimeoutSeconds = 90,
            });

        private static IReadOnlyList<DataReference> LivingUnitsOfPlayer(string snapshot, int slotID)
        {
            GameDataStore store = GameSaveSerializer.Load(snapshot);
            PlayerSlotInfo info = store.GetAllValues<PlayerSlotInfo>().First(i => i.SlotID == slotID);
            return store.GetAllDataReferences<UnitData>()
                .Where(reference =>
                {
                    UnitData unit = store.GetDataBinding<UnitData>(reference).GetValue();
                    return unit.PlayerID.Equals(info.PlayerID) && unit.GetIsAlive();
                })
                .ToList();
        }

        private static string AuthoredSnapshot(int round = 1)
        {
            GameDataStore store = ScenarioCompiler.Compile(MakeScenario(round),
                new[] { MakeArmy("Reds"), MakeArmy("Blues") });
            return SimulationService.Snapshot(store);
        }

        private static ScenarioFile MakeScenario(int round) => new ScenarioFile
        {
            Name = "Simulation seam fixture",
            Round = round,
            ActivePlayer = 0,
            Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = 4242 },
            Players = new()
            {
                new ScenarioPlayer
                {
                    Army = "reds.fdgarmy",
                    Units = new()
                    {
                        new ScenarioUnit
                        {
                            Unit = "Riflemen",
                            Models = new() { new[] { 20f, 20f }, new[] { 21f, 20f }, new[] { 22f, 20f } },
                        },
                        new ScenarioUnit
                        {
                            Unit = "Scouts",
                            Models = new() { new[] { 20f, 30f }, new[] { 21f, 30f } },
                        },
                        new ScenarioUnit
                        {
                            Unit = "Gunners",
                            Models = new() { new[] { 20f, 40f }, new[] { 21f, 40f } },
                        },
                    },
                },
                new ScenarioPlayer
                {
                    Army = "blues.fdgarmy",
                    Units = new()
                    {
                        new ScenarioUnit
                        {
                            Unit = "Riflemen",
                            Models = new() { new[] { 40f, 26f }, new[] { 41f, 26f }, new[] { 42f, 26f } },
                        },
                        new ScenarioUnit
                        {
                            Unit = "Scouts",
                            Models = new() { new[] { 40f, 36f }, new[] { 41f, 36f } },
                        },
                        new ScenarioUnit
                        {
                            Unit = "Gunners",
                            Models = new() { new[] { 40f, 46f }, new[] { 41f, 46f } },
                        },
                    },
                },
            },
        };

        private static ArmyListFile MakeArmy(string name) => new ArmyListFile
        {
            Name = name,
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Riflemen", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
                new UnitFileEntry
                {
                    Name = "Scouts", ModelCount = 2, Quality = 4, Defense = 5,
                    Weapons = new() { new WeaponFileEntry { Name = "Carbine", RangeInches = 18, Attacks = 1 } },
                },
                new UnitFileEntry
                {
                    Name = "Gunners", ModelCount = 2, Quality = 3, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Heavy Rifle", RangeInches = 30, Attacks = 2 } },
                },
            },
        };
    }
}
