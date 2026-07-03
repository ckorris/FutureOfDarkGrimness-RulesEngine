using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Dangerous terrain deals wounds but is NOT a morale-test source (corrected 2026-07-02). Shooting, melee,
    // transport destruction, etc. cause morale tests; crossing dangerous terrain never does. The earlier #009
    // wiring that ran a half-strength morale test from dangerous terrain was a rules bug and was removed.
    // Models are 1 wound each; FixedDiceRoller(1) wounds each model that crosses the dangerous zone (a roll of
    // 1) — the unit takes casualties but is never asked to take a morale test.
    [TestFixture]
    public class DangerousTerrainMoraleTests
    {
        private GameDataStore _store = null!;
        private static readonly RectangularZone DangerZone = new RectangularZone(3, 5, -2, 2);

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task ReducedToHalfByDangerousTerrain_TakesNoMoraleTest_NotShaken()
        {
            // 4 models: 2 cross the dangerous zone (and die to it), 2 move clear — leaving the unit at half
            // strength. Half strength from shooting would force a morale test, but dangerous terrain never does.
            var crossing1 = MakeModel(new Position(0, 0));
            var crossing2 = MakeModel(new Position(0, 1));
            var clear1 = MakeModel(new Position(0, 5));
            var clear2 = MakeModel(new Position(0, 6));
            var unit = MakeUnit(crossing1, crossing2, clear1, clear2);

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(crossing1, new List<Position> { new Position(8, 0) }),
                new ModelMoveEntry(crossing2, new List<Position> { new Position(8, 1) }),
                new ModelMoveEntry(clear1, new List<Position> { new Position(8, 5) }),
                new ModelMoveEntry(clear2, new List<Position> { new Position(8, 6) }),
            };

            await RunStage(unit, paths, dieValue: 1);

            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(2),
                "the two models that crossed dangerous terrain took a wound each and died.");
            Assert.That(unit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.False,
                "dangerous terrain never triggers a morale test, so the unit is not Shaken even at half strength.");
            Assert.That(clear1.GetValue().GetIsAlive(), Is.True,
                "a model that never crossed dangerous terrain is untouched.");
        }

        [Test]
        public async Task DangerousTerrainButStaysAboveHalf_NoMoraleTest_Survives()
        {
            // 4 models: only 1 crosses the dangerous zone. No morale test either way (dangerous terrain never
            // triggers one) — one model dies, the rest are untouched.
            var crossing = MakeModel(new Position(0, 0));
            var clear1 = MakeModel(new Position(0, 5));
            var clear2 = MakeModel(new Position(0, 6));
            var clear3 = MakeModel(new Position(0, 7));
            var unit = MakeUnit(crossing, clear1, clear2, clear3);

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(crossing, new List<Position> { new Position(8, 0) }),
                new ModelMoveEntry(clear1, new List<Position> { new Position(8, 5) }),
                new ModelMoveEntry(clear2, new List<Position> { new Position(8, 6) }),
                new ModelMoveEntry(clear3, new List<Position> { new Position(8, 7) }),
            };

            await RunStage(unit, paths, dieValue: 1);

            Assert.That(unit.GetValue().GetIsAlive(), Is.True);
            Assert.That(unit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.False,
                "dangerous terrain never triggers a morale test.");
            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(3),
                "one model died to terrain; the other three live.");
        }

        // Helpers

        private Task RunStage(DataBinding<UnitData> unit, List<ModelMoveEntry> paths, int dieValue)
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(dieValue));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var terrain = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) };
            var movCtx = new StubMovementContext(ctx, unit, paths, terrain);
            return stage.Enter(movCtx);
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), initialPosition, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: models.ToList());
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
