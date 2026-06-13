using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Blast(X) flows through the REAL RollToHitStage.
    // After the hit rolls land (and any injection rules fire), the stage folds the HitMultiplierSink and
    // appends synthetic hits to reach hits × X, capped at the target unit's model count — "after other
    // rules". FixedDiceRoller(6) makes the one attack a natural 6 → 1 base hit, so Blast(X) → X total
    // (clamped by model count). The stage interprets no operation.
    [TestFixture]
    public class BlastRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position TargetPos   = new Position(10, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6)); // every die shows 6
        }

        [Test]
        public async Task NoRules_OneBaseHit()
        {
            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), MakeUnit(TargetPos, modelCount: 5));
            Assert.That(TotalHits(result), Is.EqualTo(1f), "one natural 6 hits; no rule multiplies it.");
        }

        [Test]
        public async Task Blast3_TriplesHits_WithinModelCount()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 3);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos, modelCount: 5));

            Assert.That(TotalHits(result), Is.EqualTo(3f),
                "Blast(3) multiplies the 1 base hit to 3 (under the 5-model cap).");
        }

        [Test]
        public async Task Blast10_CapsAtTargetModelCount()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachBlast(attacker, 10);

            RollToHitResults result = await RunStage(attacker, MakeUnit(TargetPos, modelCount: 2));

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "Blast(10) would give 10 hits but is capped at the 2-model target unit.");
        }

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(new DetermineHitRollNeededResults(4)); // a 6 clears a 4+ threshold

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList)
            {
                total += hit.HitCount;
            }
            return total;
        }

        private static void AttachBlast(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Blast", CoreRuleCatalog.Blast,
                new RuleArgument[] { new RuleArgument.Int(x) }));

        private DataBinding<UnitData> MakeUnit(Position position, int modelCount = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: position,
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
