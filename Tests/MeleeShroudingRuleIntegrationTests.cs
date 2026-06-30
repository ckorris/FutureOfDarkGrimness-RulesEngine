using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #029 — Melee Shrouding: enemies get -3" movement (to a min. of 6") when charging this unit — the charge
    // twin of Ranged Shrouding. It fires the previously-dormant ChargeDeclaredContext; the per-target value
    // (MovementRuleQueries.EffectiveChargeDistanceAgainst) is what DefinePathStage applies as a worst-case
    // reduction to the charge budget. These tests pin the per-target query + the floor.
    [TestFixture]
    public class MeleeShroudingRuleIntegrationTests
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
        public void EffectiveChargeDistance_AgainstShroudingDefender_ReducedByThree()
        {
            DataBinding<UnitData> charger = MakeUnit();
            DataBinding<UnitData> defender = MakeUnit();
            Attach(defender, "Melee Shrouding", CoreRuleCatalog.MeleeShrouding);

            float effective = MovementRuleQueries.EffectiveChargeDistanceAgainst(
                charger.GetValue(), defender.GetValue(), baseChargeInches: 12f, _ctx.RuleEvaluator);

            Assert.That(effective, Is.EqualTo(9f), "Melee Shrouding's -3\" reduces the 12\" charge to 9\".");
        }

        [Test]
        public void EffectiveChargeDistance_PlainDefender_IsUnchanged()
        {
            DataBinding<UnitData> charger = MakeUnit();
            DataBinding<UnitData> defender = MakeUnit();

            float effective = MovementRuleQueries.EffectiveChargeDistanceAgainst(
                charger.GetValue(), defender.GetValue(), baseChargeInches: 12f, _ctx.RuleEvaluator);

            Assert.That(effective, Is.EqualTo(12f), "a defender without the rule doesn't reduce the charge.");
        }

        [Test]
        public void EffectiveChargeDistance_Floor_StopsReductionAtSix()
        {
            DataBinding<UnitData> charger = MakeUnit();
            DataBinding<UnitData> defender = MakeUnit();
            Attach(defender, "Melee Shrouding", CoreRuleCatalog.MeleeShrouding);

            // Base charge already short (8"): 8 - 3 = 5, but the rule floors the result at 6".
            float effective = MovementRuleQueries.EffectiveChargeDistanceAgainst(
                charger.GetValue(), defender.GetValue(), baseChargeInches: 8f, _ctx.RuleEvaluator);

            Assert.That(effective, Is.EqualTo(6f), "the -3\" reduction is floored at the rule's 6\" minimum.");
        }

        // The Army-Creator picker derives from CoreRuleCatalog.All; guard the rule + aura are registered.
        [Test]
        public void MeleeShrouding_IsCatalogued_AndResolvable()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (string name in new[] { "Melee Shrouding", "Melee Shrouding Aura" })
            {
                Assert.That(CoreRuleCatalog.All.Any(r => r.Name == name), Is.True, $"{name} must be in All.");
                Assert.That(resolver.TryResolve(name, out _), Is.True, $"{name} must resolve.");
            }
        }

        private static void Attach(DataBinding<UnitData> unit, string name, SpecialRuleDefinition def) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, def));

        private DataBinding<UnitData> MakeUnit()
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
