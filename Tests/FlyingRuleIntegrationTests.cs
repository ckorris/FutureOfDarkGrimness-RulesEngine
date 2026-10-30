using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #427 - OPR 3.6 Flying: "May move through units and terrain, and ignores the effects of Difficult and
    // Dangerous terrain." The engine's AllTerrain scope waives Difficult's 6" cap, Dangerous's test AND the
    // Impassable block, and Flying also moves through enemy units. "Move through ... terrain" is what lets a
    // flyer cross Impassable pieces: the 3.6 Beginner's Guide says units "may not move through Impassable
    // terrain, unless they have any rules that allow them to ignore it", and this is that rule (#427 D3).
    // The unit-level facts are pinned against the query seams; the Dangerous and Difficult ones are also run
    // through the real stage and validator, each beside a plain-unit control.
    [TestFixture]
    public class FlyingRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void Flying_IsTheCore360Wording()
        {
            Assert.That(CoreRuleCatalog.Flying.Description, Is.EqualTo(
                "May move through units and terrain, and ignores the effects of Difficult and Dangerous terrain."));
        }

        [Test]
        public void Flying_IgnoresDifficultDangerousAndImpassable_AndMovesThroughUnits_WhereStriderAndPlainDoNot()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            UnitData flyer = MakeUnit(CoreRuleCatalog.Flying).GetValue();
            UnitData strider = MakeUnit(CoreRuleCatalog.Strider).GetValue();
            UnitData plain = MakeUnit(null).GetValue();

            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(flyer, ctx.RuleEvaluator), Is.True, "Difficult");
            Assert.That(MovementRuleQueries.IgnoresAllTerrain(flyer, ctx.RuleEvaluator), Is.True,
                "Dangerous and Impassable ride the same all-terrain scope");
            Assert.That(MovementRuleQueries.CanMoveThroughEnemies(flyer, ctx.RuleEvaluator), Is.True, "units");

            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(strider, ctx.RuleEvaluator), Is.True);
            Assert.That(MovementRuleQueries.IgnoresAllTerrain(strider, ctx.RuleEvaluator), Is.False);
            Assert.That(MovementRuleQueries.CanMoveThroughEnemies(strider, ctx.RuleEvaluator), Is.False);

            Assert.That(MovementRuleQueries.IgnoresDifficultTerrain(plain, ctx.RuleEvaluator), Is.False);
            Assert.That(MovementRuleQueries.IgnoresAllTerrain(plain, ctx.RuleEvaluator), Is.False);
        }

        [Test]
        public async Task Flying_CrossingDangerousTerrain_TakesNoTest_APlainUnitDoes()
        {
            var dangerous = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)) };

            Assert.That(await WoundsFromCrossing(MakeUnit(null), dangerous), Is.EqualTo(1f),
                "control: a roll of 1 across Dangerous terrain costs a wound");
            Assert.That(await WoundsFromCrossing(MakeUnit(CoreRuleCatalog.Flying), dangerous), Is.EqualTo(0f),
                "Flying ignores the effects of Dangerous terrain");
        }

        [Test]
        public void Flying_CrossingDifficultTerrain_IsNotCappedAtSixInches()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            var difficult = new List<ITerrain> { new TerrainData(ETerrainType.Difficult, new RectangularZone(2, 4, -2, 2)) };
            DataBinding<UnitData> flyer = MakeUnit(CoreRuleCatalog.Flying);
            DataBinding<UnitData> plain = MakeUnit(null);

            bool PlainOk() => Validate(ctx, plain, difficult);
            bool FlyerOk() => Validate(ctx, flyer, difficult);

            Assert.That(PlainOk(), Is.False, "control: a 10in move through Difficult terrain breaks the 6in cap");
            Assert.That(FlyerOk(), Is.True, "Flying ignores the effects of Difficult terrain");
        }

        [Test]
        public void Flying_CrossesImpassableTerrain_APlainUnitCannot()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            var wall = new List<ITerrain> { new TerrainData(ETerrainType.Impassible, new RectangularZone(4, 6, -2, 2)) };

            Assert.That(Validate(ctx, MakeUnit(null), wall), Is.False);
            Assert.That(Validate(ctx, MakeUnit(CoreRuleCatalog.Flying), wall), Is.True,
                "'may move through ... terrain' - the rule the Beginner's Guide points Impassable terrain at");
        }

        // Validates a 10in straight move for the unit's model, with the unit's own terrain flags as the live
        // resolvers derive them from its rules.
        private bool Validate(WoundTestContext ctx, DataBinding<UnitData> unit, List<ITerrain> terrain)
        {
            var move = new ModelMoveEntry(unit.GetValue().ModelBindings[0], new List<Position> { new Position(10f, 0f) });
            return MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move }, maxDistanceInches: 20f,
                new List<EnemyModelFootprint>(),
                MovementRuleQueries.CanMoveThroughEnemies(unit.GetValue(), ctx.RuleEvaluator),
                MovementRuleQueries.IgnoresDifficultTerrain(unit.GetValue(), ctx.RuleEvaluator),
                MovementRuleQueries.IgnoresAllTerrain(unit.GetValue(), ctx.RuleEvaluator),
                terrain, out _);
        }

        private async Task<float> WoundsFromCrossing(DataBinding<UnitData> unit, List<ITerrain> terrain)
        {
            ModelData model = unit.GetValue().ModelBindings[0].GetValue();
            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(unit.GetValue().ModelBindings[0], new List<Position> { new Position(8f, 0f) }),
            };
            var ctx = new TestGameContext(_store, new FixedDiceRoller(1));
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var movCtx = new StubMovementContext(ctx, unit, paths, terrain);
            float before = model.WoundsDealt;
            await stage.Enter(movCtx);
            await MovementExecutor.ResolveDangerousTerrain(ctx, movCtx.PendingDangerousTerrain);
            return model.WoundsDealt - before;
        }

        private DataBinding<UnitData> MakeUnit(SpecialRuleDefinition? rule)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f), _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: bindings);
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
