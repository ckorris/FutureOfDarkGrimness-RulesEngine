using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Tough(X) sets each model's max wounds via the
    // real creation applicator (UnitCreationRules.Apply), the same call FDGServer makes at army-load.
    // Tough fires Lifecycle_OnUnitCreated -> SetMaxWounds(Arg(0)); the MaxWoundsSink folds it and the
    // applicator calls IModel.SetMaxWounds on every model. Once TotalWounds reflects Tough, the rest
    // of the wound system (RemainingWounds, IsAlive, AssignWoundsResults absorption) handles multi-
    // wound models with no further code.
    [TestFixture]
    public class ToughRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _evaluator = new RuleEvaluator(new FixedDiceRoller(4)); // creation rules don't roll
        }

        [Test]
        public void ToughUnit_GetsMaxWoundsFromArgument()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3);
            AttachTough(unit, x: 3);

            UnitCreationRules.Apply(unit.GetValue(), _evaluator);

            foreach (IModel model in unit.GetValue().Models)
            {
                Assert.That(model.TotalWounds, Is.EqualTo(3f), "Tough(3) gives each model 3 wounds.");
            }
            Assert.That(unit.GetValue().RemainingWounds, Is.EqualTo(9f),
                "3 models × 3 wounds = 9 wounds of unit capacity.");
        }

        [Test]
        public void NoTough_ModelsKeepOneWound()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3);

            UnitCreationRules.Apply(unit.GetValue(), _evaluator);

            foreach (IModel model in unit.GetValue().Models)
            {
                Assert.That(model.TotalWounds, Is.EqualTo(1f), "no Tough → the default one wound.");
            }
        }

        private static void AttachTough(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(
                new ResolvedRule("Tough", CoreRuleCatalog.Tough, new RuleArgument[] { new RuleArgument.Int(x) }));

        private DataBinding<UnitData> MakeUnit(int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    specialRules: new List<SpecialRule>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
