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
    // Models are 1 wound each; every crossing model now rolls together in one batch, so the roller must honor
    // rollCount -- FixedFaceDiceRoller(1) makes every die in the batch a 1, wounding each crossing model. The
    // unit takes casualties but is never asked to take a morale test.
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

        [Test]
        public async Task ProbabilisticRoller_DealsFractionalWoundsAcrossTheBatch()
        {
            // 3 models all cross the dangerous zone. Under the probabilistic roller a single batch of 3 d6
            // yields the expected 3/6 = 0.5 ones, spread evenly (0.5/3 wound each) -- fractional wounds that
            // don't kill a 1-wound model. This is exactly what N separate decisive per-model rolls could not
            // produce, and why batching matters for the probabilistic roller.
            var m1 = MakeModel(new Position(0, 0));
            var m2 = MakeModel(new Position(0, 1));
            var m3 = MakeModel(new Position(0, -1));
            var unit = MakeUnit(m1, m2, m3);

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(m1, new List<Position> { new Position(8, 0) }),
                new ModelMoveEntry(m2, new List<Position> { new Position(8, 1) }),
                new ModelMoveEntry(m3, new List<Position> { new Position(8, -1) }),
            };

            var settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            var ctx = new TestGameContext(_store, new ProbabilisticDiceRoller(), settings: settings);
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var terrain = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) };
            await stage.Enter(new StubMovementContext(ctx, unit, paths, terrain));

            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(3),
                "fractional wounds from the probabilistic batch don't kill a 1-wound model.");
            float total = m1.GetValue().WoundsDealt + m2.GetValue().WoundsDealt + m3.GetValue().WoundsDealt;
            Assert.That(total, Is.EqualTo(0.5f).Within(0.001f),
                "the batch of 3 dice deals an expected 3/6 = 0.5 wounds total, spread across the models.");
        }

        // Helpers

        private Task RunStage(DataBinding<UnitData> unit, List<ModelMoveEntry> paths, int dieValue)
        {
            // FixedFaceDiceRoller honors rollCount so the single batched Roll(6, N) reports N dice on the
            // fixed face (FixedDiceRoller collapses every batch to a single die).
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(dieValue));
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
