using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #282: the commit-time overlap check behind every mandatory model placement (deploy, re-deploy,
    // Scout, Ambush arrival, spillout). The stages used to commit whatever a resolver returned - the
    // engine gate checked only zone containment - so a resolver-side failure could silently deploy one
    // unit inside another (the YellowDeployedOverGreen save). The guard must pass clean placements
    // through untouched, and re-place (with a warning) a set that interpenetrates on-table models.
    [TestFixture]
    public class PlacementCommitGuardTests
    {
        private GameDataStore _store = null!;
        private RecordingTextOutput _log = null!;
        private TestGameContext _ctx = null!;
        private PlayerID _me;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), textOutput: _log);
            _me = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task CleanPlacements_PassThroughUntouched()
        {
            CreatePlacedUnit("Bystanders", new Position(30f, 24f));
            (PlaceObjectsRequest<ModelData> request, List<DataBinding<ModelData>> models) =
                IncomingUnit("Newcomers");

            // Well clear of the bystanders.
            var placements = PlacementsAt(models, new Position(10f, 10f));
            List<PlacedObjectEntry<ModelData>> result =
                await PlacementCommitGuard.EnsureClear(_ctx, request, placements);

            Assert.That(result, Is.SameAs(placements), "clean placements must commit untouched");
            Assert.That(_log.Lines, Is.Empty, "no warning for a legal placement");
        }

        [Test]
        public async Task OverlappingPlacements_AreReplacedClearAndWarned()
        {
            List<DataBinding<ModelData>> bystanders = CreatePlacedUnit("Bystanders", new Position(30f, 24f));
            (PlaceObjectsRequest<ModelData> request, List<DataBinding<ModelData>> models) =
                IncomingUnit("Newcomers");

            // Directly on top of the bystanders - the corruption the guard exists to stop.
            var placements = PlacementsAt(models, new Position(30f, 24f));
            List<PlacedObjectEntry<ModelData>> result =
                await PlacementCommitGuard.EnsureClear(_ctx, request, placements);

            Assert.That(result, Is.Not.SameAs(placements));
            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                float r = entry.Binding.GetValue().BaseRadiusInches;
                foreach (DataBinding<ModelData> b in bystanders)
                {
                    ModelData other = b.GetValue();
                    float dx = entry.Position.x - other.Position.x, dz = entry.Position.z - other.Position.z;
                    float gap = MathF.Sqrt(dx * dx + dz * dz) - r - other.BaseRadiusInches;
                    Assert.That(gap, Is.GreaterThanOrEqualTo(-PlacementCommitGuard.OverlapToleranceInches),
                        "re-placed models must be clear of the unit they overlapped");
                }
                Assert.That(request.DeploymentZone.IsPointWithinZone(
                        new Float2(entry.Position.x, entry.Position.z)), Is.True,
                    "the repair must honour the request's own zone");
            }
            Assert.That(_log.Lines.Any(l => l.Contains("WARNING") && l.Contains("Newcomers")
                    && l.Contains("Bystanders")), Is.True,
                "the violation must be named in the game log - a recurrence is a diagnosis, not a mystery");
        }

        // A 3-model unit already standing on the table.
        private List<DataBinding<ModelData>> CreatePlacedUnit(string name, Position centre)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 3; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(),
                    new Position(centre.x + i * 1.1f, centre.z), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(m)));
            }
            _store.Create(new UnitData(_me, name, 4, 4, models));
            return models;
        }

        // A 3-model unit still at the origin, plus the deployment-shaped request that places it.
        private (PlaceObjectsRequest<ModelData> request, List<DataBinding<ModelData>> models)
            IncomingUnit(string name)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 3; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(m)));
            }
            _store.Create(new UnitData(_me, name, 4, 4, models));
            var zone = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            return (new PlaceObjectsRequest<ModelData>(_me, "Place Unit Models", zone, models), models);
        }

        private static List<PlacedObjectEntry<ModelData>> PlacementsAt(
            List<DataBinding<ModelData>> models, Position centre)
        {
            var placements = new List<PlacedObjectEntry<ModelData>>();
            for (int i = 0; i < models.Count; i++)
                placements.Add(new PlacedObjectEntry<ModelData>(models[i],
                    new Position(centre.x + i * 1.1f, centre.z), null));
            return placements;
        }

        private sealed class RecordingTextOutput : ITextOutput
        {
            public List<string> Lines { get; } = new();
            public void Log(string message, TextColor? color = null) => Lines.Add(message);
        }
    }
}
