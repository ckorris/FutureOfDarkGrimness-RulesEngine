using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class DangerousTerrainWoundTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public async Task RollOf1_CrossesDangerous_DealsOneWound()
        {
            var (model, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2), from: new Position(0, 0), to: new Position(8, 0));
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };
            float woundsBefore = model.GetValue().WoundsDealt;

            await RunStage(diceValue: 1, terrain: dangerous, paths: paths);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore + 1));
        }

        [Test]
        public async Task RollOf2_CrossesDangerous_NoWound()
        {
            var (model, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2), from: new Position(0, 0), to: new Position(8, 0));
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };
            float woundsBefore = model.GetValue().WoundsDealt;

            await RunStage(diceValue: 2, terrain: dangerous, paths: paths);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        [Test]
        public async Task PathMissesDangerous_NoWound()
        {
            // Model moves at z=5, well above the dangerous zone (z -2..2).
            DataBinding<ModelData> model = MakeModel(new Position(0, 5));
            var move = new ModelMoveEntry(model, new List<Position> { new Position(8, 5) });
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };
            float woundsBefore = model.GetValue().WoundsDealt;

            await RunStage(diceValue: 1, terrain: dangerous, paths: new List<ModelMoveEntry> { move });

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        [Test]
        public async Task NoDangerousTerrain_NoWound()
        {
            var (model, paths) = MakePathThrough(dangerousZone: new RectangularZone(3, 5, -2, 2), from: new Position(0, 0), to: new Position(8, 0));
            var noDangerous = new List<ITerrain> { new TerrainData(ETerrainType.Cover, new RectangularZone(3, 5, -2, 2)) };
            float woundsBefore = model.GetValue().WoundsDealt;

            await RunStage(diceValue: 1, terrain: noDangerous, paths: paths);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        [Test]
        public async Task NoPaths_NoWound()
        {
            var model = MakeModel(new Position(0, 0));
            var unit = MakeUnit(new List<DataBinding<ModelData>> { model });
            float woundsBefore = model.GetValue().WoundsDealt;
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };

            var ctx = new TestGameContext(_store, new FixedDiceRoller(1));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var movCtx = new StubMovementContext(ctx, unit, paths: null, terrain: dangerous);
            await stage.Enter(movCtx);

            Assert.That(model.GetValue().WoundsDealt, Is.EqualTo(woundsBefore));
        }

        // Helpers

        private Task RunStage(int diceValue, List<ITerrain> terrain, List<ModelMoveEntry> paths)
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(diceValue));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var unit = MakeUnit(paths.Select(p => p.Model).ToList());
            var movCtx = new StubMovementContext(ctx, unit, paths, terrain);
            return stage.Enter(movCtx);
        }

        private (DataBinding<ModelData> model, List<ModelMoveEntry> paths) MakePathThrough(
            RectangularZone dangerousZone, Position from, Position to)
        {
            DataBinding<ModelData> model = MakeModel(from);
            var move = new ModelMoveEntry(model, new List<Position> { to });
            return (model, new List<ModelMoveEntry> { move });
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            var modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: initialPosition,
                gameDataStore: _store);
            var reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }

        private DataBinding<UnitData> MakeUnit(List<DataBinding<ModelData>> modelBindings)
        {
            var playerID = new PlayerID(Guid.NewGuid());
            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            var reference = _store.Create(unit);
            return _store.GetDataBinding<UnitData>(reference);
        }
    }

    // Stub IMovementActionContext for DangerousTerrainWoundTests.
    internal class StubMovementContext : IMovementActionContext
    {
        private readonly IGameContext _gameContext;
        private readonly DataBinding<UnitData> _unit;
        private readonly List<ModelMoveEntry>? _paths;
        private readonly List<ITerrain> _terrain;

        public IGameContext GameContext => _gameContext;
        public DataBinding<UnitData> MovingUnit => _unit;
        public float MaxAdvanceDistance => 12f;
        public float MaxRushDistance => 12f;
        public float MaxChargeDistance => 12f;
        public bool TryGetModelMoveBudget(IModel model, out float advance, out float rush, out float charge)
        {
            advance = MaxAdvanceDistance; rush = MaxRushDistance; charge = MaxChargeDistance; return true;
        }
        public List<ITerrain> RelevantTerrain => _terrain;

        public StubMovementContext(IGameContext ctx, DataBinding<UnitData> unit,
            List<ModelMoveEntry>? paths, List<ITerrain> terrain)
        {
            _gameContext = ctx;
            _unit = unit;
            _paths = paths;
            _terrain = terrain;
        }

        public bool TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths)
        {
            if (_paths == null) { paths = null!; return false; }
            paths = _paths;
            return true;
        }

        public bool TryGetMovementDistance(out float distance) { distance = 0f; return false; }
        public void SubmitValidPathTemplate(List<ModelMoveEntry> paths) { }
    }
}
