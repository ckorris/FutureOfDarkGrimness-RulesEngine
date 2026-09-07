using FDG.Ai;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.SaveLoad;
using FDG.Simulation;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #396: the typed whole-store copy (<see cref="StoreClone"/>) that replaced the search's JSON
    /// round trip. The contract is equivalence with the serializer - a clone IS Load(Save(store)) -
    /// so the pins are: byte-identical saves on played boards, independence from the source, the same
    /// references handed out afterwards, and a simulation / a search that agree byte for byte and
    /// choice for choice whichever path they run on.
    /// </summary>
    [TestFixture]
    public class StoreCloneTests
    {
        private static string Fixture() => TacticianActionSpaceTests.Fixture.Snapshot(4, objectives: 3);

        private static SimulationService Sim(int seed, EAiProfile profile = EAiProfile.Tactician) =>
            new(new SimulationService.SimulationOptions { Profile = profile, Seed = seed, TimeoutSeconds = 120 });

        [Test]
        public void Clone_OfACompiledBoard_SavesByteForByteLikeItsSource()
        {
            GameDataStore source = GameSaveSerializer.Load(Fixture());
            GameDataStore clone = StoreClone.Clone(source);

            Assert.That(GameSaveSerializer.Save(clone), Is.EqualTo(GameSaveSerializer.Save(source)));
            Assert.That(GameSaveSerializer.Save(StoreClone.Clone(clone)), Is.EqualTo(GameSaveSerializer.Save(source)),
                "a clone of a clone is still the same state");
        }

        /// <summary>
        /// The real pin: a board that has been PLAYED carries tokens, wounds, moved models, a
        /// captured flow state, destroyed slots - every kind of entry the cloner must copy. Cloned
        /// at each boundary of a natural line, against the serializer as the oracle.
        /// </summary>
        [Test]
        [CancelAfter(180_000)]
        public async Task Clone_AtEveryBoundaryOfAPlayedLine_SavesByteForByteLikeTheLiveStore()
        {
            var driver = new CloneAtEveryBoundary(activations: 6);
            SimulationService.SimulationResult result = await Sim(seed: 17).Run(Fixture(), driver);

            Assert.That(result.ReachedEndOfLine, Is.True, result.Note);
            Assert.That(driver.Boundaries, Is.EqualTo(7), "six activations plus the stop boundary");
            Assert.That(driver.Mismatches, Is.Empty);
        }

        private sealed class CloneAtEveryBoundary : SimulationService.ILineDriver
        {
            private readonly int _activations;
            public int Boundaries;
            public readonly List<string> Mismatches = new();

            public CloneAtEveryBoundary(int activations) => _activations = activations;

            public SimulationService.LineStep AtBoundary(SimulationService.LineBoundary boundary)
            {
                Boundaries++;
                var live = (GameDataStore)boundary.State.DataStore;
                string expected = GameSaveSerializer.Save(live);
                string actual = GameSaveSerializer.Save(StoreClone.Clone(live));
                if (actual != expected) Mismatches.Add($"boundary {boundary.Index}: clone differs from the live store");
                return boundary.Index < _activations ? SimulationService.LineStep.Natural : SimulationService.LineStep.Stop;
            }
        }

        [Test]
        public void Clone_IsIndependentOfItsSource()
        {
            GameDataStore source = GameSaveSerializer.Load(Fixture());
            GameDataStore clone = StoreClone.Clone(source);

            UnitData sourceUnit = source.GetAllValues<UnitData>().First();
            UnitData cloneUnit = clone.GetAllValues<UnitData>().First(u => u.ID == sourceUnit.ID);
            ModelData sourceModel = sourceUnit.ModelBindings[0].GetValue();
            ModelData cloneModel = cloneUnit.ModelBindings[0].GetValue();
            Assert.That(cloneModel, Is.Not.SameAs(sourceModel));
            Assert.That(cloneModel.Position.x, Is.EqualTo(sourceModel.Position.x));

            // Moving and wounding the clone leaves the source where it was...
            cloneModel.SetPosition(new Position(sourceModel.Position.x + 5f, sourceModel.Position.z));
            int sourceFired = 0, cloneFired = 0;
            sourceUnit.OnWoundsDealt += (_, _) => sourceFired++;
            cloneUnit.OnWoundsDealt += (_, _) => cloneFired++;
            cloneModel.DealWounds(1f);

            Assert.That(sourceModel.Position.x, Is.EqualTo(cloneModel.Position.x - 5f).Within(0.001f));
            Assert.That(sourceModel.WoundsDealt, Is.EqualTo(0f));
            Assert.That(cloneModel.WoundsDealt, Is.EqualTo(1f));
            // ...and the clone's own wound aggregation is wired (the loader's rewire step), the source's untouched.
            Assert.That(cloneFired, Is.EqualTo(1), "the clone unit hears its model's wound");
            Assert.That(sourceFired, Is.EqualTo(0), "the source unit does not hear the clone's model");

            // Tokens are copied, not shared.
            cloneUnit.Tokens.AddToken(new Rules.Tokens.Token(Rules.Foundation.TokenType.ActivatedThisRound, 1,
                new Rules.Foundation.TokenClearTrigger.ManualOnly()));
            Assert.That(sourceUnit.Tokens.HasToken(Rules.Foundation.TokenType.ActivatedThisRound), Is.False);
        }

        /// <summary>
        /// A save records only occupied slots, so a loaded store's free slots are at generation 0
        /// and the next Create there hands out generation 1. The clone mirrors that rather than
        /// copying the live generation counters, so references stay identical across the two paths.
        /// </summary>
        [Test]
        public void Clone_HandsOutTheSameReferences_AsALoadedStore_AfterCreate()
        {
            GameDataStore source = GameSaveSerializer.Load(Fixture());
            // Recycle a slot so the live counter runs ahead of what a save can carry.
            DataReference scratch = source.Create(41);
            source.Destroy(scratch);
            scratch = source.Create(42);
            source.Destroy(scratch);

            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(source));
            GameDataStore clone = StoreClone.Clone(source);

            DataReference fromLoaded = loaded.Create(7);
            DataReference fromClone = clone.Create(7);
            Assert.That(fromClone, Is.EqualTo(fromLoaded), "same slot, same generation, on both paths");
            Assert.That(fromClone.Generation, Is.EqualTo(1));
        }

        /// <summary>A registered type the cloner has no typed copy for goes through the serializer, so a
        /// new component type can never make the search silently drop state.</summary>
        [Test]
        public void Clone_FallsBackToTheSerializer_ForATypeWithoutATypedCloner()
        {
            GameDataStore source = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(8)
                .RegisterType<Extra>(4)
                .Build();
            DataReference counter = source.Create(3);
            source.Create(new Extra { Name = "spare", Counter = source.GetDataBinding<int>(counter) });

            GameDataStore clone = StoreClone.Clone(source);

            Extra copied = clone.GetAllValues<Extra>().Single();
            Assert.That(copied.Name, Is.EqualTo("spare"));
            Assert.That(copied.Counter!.GetValue(), Is.EqualTo(3));
            Assert.That(copied.Counter, Is.SameAs(clone.GetDataBinding<int>(counter)), "bound into the clone");
            Assert.That(GameSaveSerializer.Save(clone), Is.EqualTo(GameSaveSerializer.Save(source)));
        }

        public class Extra
        {
            public string Name = string.Empty;
            public DataBinding<int>? Counter;
        }

        // --- the two paths through the search's own seams ---------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task Simulation_ThroughTheCloneAndTheSerializer_AgreesByteForByte()
        {
            string json = Fixture();
            var serializer = new JsonSnapshot(json);
            StoreSnapshot clone = StoreSnapshot.Capture(GameSaveSerializer.Load(json));
            Assert.That(clone.ToJson(), Is.EqualTo(json), "the captured state is the state");

            SimulationService.SimulationResult viaJson = await Sim(seed: 909).RunNatural(serializer, 3);
            SimulationService.SimulationResult viaClone = await Sim(seed: 909).RunNatural(clone, 3);

            Assert.That(viaJson.ReachedEndOfLine, Is.True, viaJson.Note);
            Assert.That(viaClone.ReachedEndOfLine, Is.True, viaClone.Note);
            Assert.That(viaClone.State, Is.InstanceOf<StoreSnapshot>(), "a line hands back a typed copy");
            Assert.That(viaClone.Snapshot, Is.EqualTo(viaJson.Snapshot),
                "three natural activations from the same state under the same seed: identical either way");
            Assert.That(viaClone.ActingPlayerAtEnd, Is.EqualTo(viaJson.ActingPlayerAtEnd));
        }

        [Test]
        [CancelAfter(300_000)]
        public async Task Search_ThroughTheCloneAndTheSerializer_ChoosesIdentically()
        {
            string json = Fixture();
            var options = new UctOptions
            {
                RootSeed = 99,
                Workers = 2,
                Iterations = 3,
                Tree = new SearchOptions { InSimProfile = EAiProfile.Tactician, TimeoutSeconds = 120 },
            };

            SearchResult viaJson = await UctSearch.RunAsync(json, options, new HandWeightedEvaluator());
            SearchResult viaClone = await UctSearch.RunAsync(
                StoreSnapshot.Capture(GameSaveSerializer.Load(json)), options, new HandWeightedEvaluator());

            Assert.That(viaJson.Choice, Is.Not.Null);
            Assert.That(viaClone.Choice!.Label, Is.EqualTo(viaJson.Choice!.Label), "same choice");
            Assert.That(Distribution(viaClone), Is.EqualTo(Distribution(viaJson)),
                "same visits and values at the root: the search cannot tell the two paths apart");
            Assert.That(viaClone.Nodes, Is.EqualTo(viaJson.Nodes));
        }

        private static string Distribution(SearchResult result) =>
            string.Join("|", result.Root.Select(s => $"{s.UnitIndex}.{s.EdgeIndex}:{s.Visits}:{s.Value:F4}"));
    }
}
