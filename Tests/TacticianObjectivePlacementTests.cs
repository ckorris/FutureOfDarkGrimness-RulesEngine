using System;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A4b-2 — profile-driven objective placement: a firebase army clusters the markers,
    // a melee army races them wide; every placement is legal (validator-checked in the resolver).
    [TestFixture]
    public class TacticianObjectivePlacementTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task ShootingArmy_ClustersTheMarkers()
        {
            MakeArmy(Rifle());
            List<Position> placed = await PlaceMarkers(3);

            float spread = placed.Max(p => p.x) - placed.Min(p => p.x);
            Assert.That(spread, Is.LessThanOrEqualTo(2f * 9f + 1f),
                "a firebase army keeps every marker within one gun line's coverage");
        }

        [Test]
        public async Task MeleeArmy_SpreadsTheMarkersWide()
        {
            MakeArmy(Blade());
            List<Position> placed = await PlaceMarkers(3);

            float spread = placed.Max(p => p.x) - placed.Min(p => p.x);
            Assert.That(spread, Is.GreaterThanOrEqualTo(24f),
                "a mobile army races the markers wide across the band");
        }

        [Test]
        public async Task Placements_RespectMinSeparation()
        {
            MakeArmy(Rifle());
            List<Position> placed = await PlaceMarkers(4);

            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                {
                    float dx = placed[i].x - placed[j].x, dz = placed[i].z - placed[j].z;
                    Assert.That(MathF.Sqrt(dx * dx + dz * dz), Is.GreaterThanOrEqualTo(9f - 0.01f),
                        "markers respect the separation rule");
                }
        }

        // --- fixtures ---

        // Place markers one at a time, committing each to the store like PlaceObjectivesStage does,
        // so later placements see the earlier ones.
        private async Task<List<Position>> PlaceMarkers(int total)
        {
            var resolver = new TacticianPlaceObjectiveResolver(_tableState);
            var band = new RectangularZone(left: 0f, right: 48f, bottom: 18f, top: 30f);
            var placed = new List<Position>();
            for (int i = 1; i <= total; i++)
            {
                Position p = await resolver.Resolve(new PlaceObjectiveRequest(
                    _player, "Place Objective", i, total, band, minSeparationInches: 9f));
                placed.Add(p);
                _store.Create(new ObjectiveData(p, _store));
            }
            return placed;
        }

        private static Weapon Rifle() => new Weapon("Rifle", 24f, 1, 0);
        private static Weapon Blade() => new Weapon("Blade", 0f, 2, 0);

        private void MakeArmy(Weapon weapon)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(10f + i, 5f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(_player, "Army", quality: 4, defense: 4, modelBindings: models);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
        }
    }
}
