using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves the extra-hit rules (Surge / Relentless /
    // Furious) flow through the REAL RollToHitStage. On an unmodified 6 the stage fires the
    // Shooting_OnHitRollComplete "when", the RuleEvaluator queues InsertExtraHits, the
    // HitInjectionSink folds the total, and the stage appends synthetic hits to the successful-hit
    // list — the scalar->IDiceResults bridge. FixedDiceRoller(6) makes every die a natural 6, so
    // each case is "2 total hits with the rule firing, 1 without."
    [TestFixture]
    public class ExtraHitRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position FarPos      = new Position(20, 5);  // > 9" base-to-base
        private static readonly Position NearPos     = new Position(3, 5);   // <= 9"

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6)); // every die shows 6
        }

        [Test]
        public async Task NoRules_OneBaseHit_NoExtras()
        {
            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), MakeUnit(FarPos));
            Assert.That(TotalHits(result), Is.EqualTo(1f), "one natural 6 hits; no rule adds extras.");
        }

        [Test]
        public async Task Surge_OnUnmodified6_AddsExtraHit()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachSurge(attacker);

            RollToHitResults result = await RunStage(attacker, MakeUnit(FarPos));

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "Surge injects one extra hit per natural 6 (1 base + 1 injected).");
        }

        [Test]
        public async Task Relentless_Beyond9_AddsExtraHit()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachRelentless(attacker);

            RollToHitResults result = await RunStage(attacker, MakeUnit(FarPos));

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "beyond 9\", Relentless injects an extra hit on a natural 6.");
        }

        [Test]
        public async Task Relentless_Within9_NoExtra()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachRelentless(attacker);

            RollToHitResults result = await RunStage(attacker, MakeUnit(NearPos));

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "within 9\", Relentless's distance gate fails — no extra hit.");
        }

        [Test]
        public async Task Furious_ChargingInMelee_AddsExtraHit()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachFurious(attacker);

            RollToHitResults result = await RunStage(attacker, MakeUnit(FarPos), isMelee: true, isCharging: true);

            Assert.That(TotalHits(result), Is.EqualTo(2f),
                "charging in melee, Furious injects an extra hit on a natural 6.");
        }

        [Test]
        public async Task Furious_MeleeNotCharging_NoExtra()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachFurious(attacker);

            RollToHitResults result = await RunStage(attacker, MakeUnit(FarPos), isMelee: true, isCharging: false);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "a strike-back is melee but not charging — Furious's charge gate fails.");
        }

        [Test]
        public async Task Furious_InShooting_NoExtra()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            AttachFurious(attacker);

            RollToHitResults result = await RunStage(attacker, MakeUnit(FarPos), isMelee: false);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "in shooting, Furious's melee gate fails — no extra hit.");
        }

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, bool isMelee = false, bool isCharging = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: isCharging);
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

        private static void AttachSurge(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Surge", CoreRuleCatalog.Surge));

        private static void AttachRelentless(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Relentless", CoreRuleCatalog.Relentless));

        private static void AttachFurious(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Furious", CoreRuleCatalog.Furious));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
