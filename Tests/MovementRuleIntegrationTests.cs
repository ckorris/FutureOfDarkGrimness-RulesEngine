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

        private static void AttachFast(DataBinding<UnitData> unit)
        {
            var fast = new SpecialRuleDefinition("Fast",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Advance),
                        new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                        ELifetime.ThisActivation),
                },
                System.Array.Empty<ActivatedAbility>());

            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Fast", fast));
        }

        private static void AttachSlow(DataBinding<UnitData> unit)
        {
            var slow = new SpecialRuleDefinition("Slow",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Advance),
                        new Effect.MovementBonus(EActionType.Advance, DistanceInches: -2f),
                        ELifetime.ThisActivation),
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Rush),
                        new Effect.MovementBonus(EActionType.Rush, DistanceInches: -4f),
                        ELifetime.ThisActivation),
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Charge),
                        new Effect.MovementBonus(EActionType.Charge, DistanceInches: -4f),
                        ELifetime.ThisActivation),
                },
                System.Array.Empty<ActivatedAbility>());

            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Slow", slow));
        }

        private DataBinding<UnitData> MakeUnit()
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                specialRules: new List<SpecialRule>(),
                initialPosition: new Position(0, 0),
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
