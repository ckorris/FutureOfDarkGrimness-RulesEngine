using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Fast/Slow flow through the REAL
    // MovementActionContext. Its constructor fires the Movement_OnMoveActionDeclared "when"
    // once per action type, the RuleEvaluator evaluates the Actor seat, and the
    // MovementModifierSink folds the result into each budget — none of it interpreted by the
    // context. Baselines are taken from a no-rule unit so the test survives constant changes.
    [TestFixture]
    public class MovementRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public void FastUnit_AddsTwoInchesToAdvance_LeavesRushAndChargeAlone()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> fast = MakeUnit();
            AttachFast(fast);
            var context = new MovementActionContext(_ctx, fast);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 2f).Within(0.001f),
                "Fast adds +2\" to the Advance budget.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance).Within(0.001f),
                "Fast's only entry is gated on Advance, so Rush is untouched.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance).Within(0.001f),
                "Fast's only entry is gated on Advance, so Charge is untouched.");
        }

        [Test]
        public void SlowUnit_ReducesAllThreeBudgets()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> slow = MakeUnit();
            AttachSlow(slow);
            var context = new MovementActionContext(_ctx, slow);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance - 2f).Within(0.001f),
                "Slow subtracts 2\" from Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance - 4f).Within(0.001f),
                "Slow subtracts 4\" from Rush.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance - 4f).Within(0.001f),
                "Slow subtracts 4\" from Charge.");
        }

        [Test]
        public void AgileUnit_AddsOneToAdvance_TwoToRushAndCharge()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> agile = MakeUnit();
            agile.GetValue().AttachRuleDefinition(new ResolvedRule("Agile", CoreRuleCatalog.Agile));
            var context = new MovementActionContext(_ctx, agile);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 1f).Within(0.001f),
                "Agile adds +1\" to Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 2f).Within(0.001f),
                "Agile adds +2\" to Rush.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 2f).Within(0.001f),
                "Agile adds +2\" to Charge.");
        }

        [Test]
        public void QuickUnit_AddsTwoToAllThreeBudgets()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> quick = MakeUnit();
            quick.GetValue().AttachRuleDefinition(new ResolvedRule("Quick", CoreRuleCatalog.Quick));
            var context = new MovementActionContext(_ctx, quick);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 2f).Within(0.001f),
                "Quick adds +2\" to Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 2f).Within(0.001f),
                "Quick adds +2\" to Rush.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 2f).Within(0.001f),
                "Quick adds +2\" to Charge.");
        }

        [Test]
        public void RapidAdvance_AddsFourToAdvanceOnly()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rapid Advance", CoreRuleCatalog.RapidAdvance));
            var context = new MovementActionContext(_ctx, unit);

            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance + 4f).Within(0.001f),
                "Rapid Advance adds +4\" to Advance.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance).Within(0.001f),
                "Rapid Advance is Advance-only; Rush untouched.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance).Within(0.001f),
                "Rapid Advance is Advance-only; Charge untouched.");
        }

        [Test]
        public void RapidRush_AddsSixToRushOnly()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rapid Rush", CoreRuleCatalog.RapidRush));
            var context = new MovementActionContext(_ctx, unit);

            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance + 6f).Within(0.001f),
                "Rapid Rush adds +6\" to Rush.");
            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f),
                "Rapid Rush is Rush-only; Advance untouched.");
            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance).Within(0.001f),
                "Rapid Rush is Rush-only; Charge untouched.");
        }

        [Test]
        public void RapidCharge_AddsFourToChargeOnly()
        {
            var baseline = new MovementActionContext(_ctx, MakeUnit());

            DataBinding<UnitData> unit = MakeUnit();
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rapid Charge", CoreRuleCatalog.RapidCharge));
            var context = new MovementActionContext(_ctx, unit);

            Assert.That(context.MaxChargeDistance, Is.EqualTo(baseline.MaxChargeDistance + 4f).Within(0.001f),
                "Rapid Charge adds +4\" to Charge.");
            Assert.That(context.MaxAdvanceDistance, Is.EqualTo(baseline.MaxAdvanceDistance).Within(0.001f),
                "Rapid Charge is Charge-only; Advance untouched.");
            Assert.That(context.MaxRushDistance, Is.EqualTo(baseline.MaxRushDistance).Within(0.001f),
                "Rapid Charge is Charge-only; Rush untouched.");
        }

        private static void AttachFast(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Fast", CoreRuleCatalog.Fast));

        private static void AttachSlow(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Slow", CoreRuleCatalog.Slow));

        private DataBinding<UnitData> MakeUnit()
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0),
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
