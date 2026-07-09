using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class ChooseActionPassDisableTests
    {
        [Test]
        public void GetCanPass_NoMovement_True()
        {
            var (ctx, unitCtx) = Build();

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True);
            Assert.That(reason, Is.Null);
        }

        [Test]
        public void GetCanPass_MovedWithinRush_True()
        {
            var (ctx, unitCtx) = Build();
            unitCtx.RegisterMoveFinished(GameWideConstants.RUSH_DISTANCE_INCHES); // exactly Rush — still allowed

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out _);

            Assert.That(canPass, Is.True);
        }

        [Test]
        public void GetCanPass_MovedBeyondRush_False()
        {
            var (ctx, unitCtx) = Build();
            //Simulate a Charge-distance move that exceeded Rush (only legal because the validator
            //confirmed at least one model ended in melee).
            unitCtx.RegisterMoveFinished(GameWideConstants.RUSH_DISTANCE_INCHES + 2f);

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.False);
            Assert.That(reason, Is.Not.Null.And.Contains("Rush"));
        }

        // The gate must use the unit's REAL Rush allowance, movement rules included, not the bare
        // default — an Agile unit (+2" Rush) that legally rushed 14" may still Pass. Regression: the
        // gate compared against the default 12" and locked such units into their remaining layered
        // actions (e.g. Cast) until they ran dry.
        [Test]
        public void GetCanPass_RushBonusRule_MovedWithinBoostedRush_True()
        {
            var (ctx, unitCtx) = Build(unit =>
                unit.GetValue().AttachRuleDefinition(new ResolvedRule("Agile", CoreRuleCatalog.Agile)));
            unitCtx.RegisterMoveFinished(GameWideConstants.RUSH_DISTANCE_INCHES + 2f); // Agile's exact cap

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"a move within the rule-boosted Rush allowance must not gate Pass. Reason given: {reason}");
        }

        [Test]
        public void GetCanPass_RushBonusRule_MovedBeyondBoostedRush_False()
        {
            var (ctx, unitCtx) = Build(unit =>
                unit.GetValue().AttachRuleDefinition(new ResolvedRule("Agile", CoreRuleCatalog.Agile)));
            unitCtx.RegisterMoveFinished(GameWideConstants.RUSH_DISTANCE_INCHES + 2.5f); // past even Agile's cap

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.False, "beyond even the boosted allowance the engage gate still applies.");
            Assert.That(reason, Is.Not.Null.And.Contains("Rush"));
        }

        // Once the unit has attacked, it has followed through on the engage obligation — the flow loops
        // back to Choose Action after the melee, and the distance gate must not re-fire on the same
        // move. Regression: a caster that charged beyond-Rush was locked out of Pass AFTER its melee,
        // forced to Cast until its spell tokens ran dry.
        [Test]
        public void GetCanPass_MovedBeyondRush_ButAlreadyAttacked_True()
        {
            var (ctx, unitCtx) = Build();
            unitCtx.RegisterMoveFinished(GameWideConstants.RUSH_DISTANCE_INCHES + 2f);
            unitCtx.RegisterAttackedFinished();

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"a unit that already engaged must be allowed to Pass. Reason given: {reason}");
        }

        // #093: a per-model movement rule (a joined hero's own Agile) raises that model's budget, and
        // MoveDistance is the max over models — so the gate must honor the per-model allowance too.
        [Test]
        public void GetCanPass_ModelWithOwnRushBonus_MovedWithinItsBudget_True()
        {
            var (ctx, unitCtx) = Build(unit =>
                ((ModelData)unit.GetValue().Models.First()).AttachRuleDefinition(
                    new ResolvedRule("Agile", CoreRuleCatalog.Agile)));
            unitCtx.RegisterMoveFinished(GameWideConstants.RUSH_DISTANCE_INCHES + 2f);

            bool canPass = ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason);

            Assert.That(canPass, Is.True,
                $"a model that legally ranged ahead on its own budget must not gate Pass. Reason given: {reason}");
        }

        private static (TestGameContext ctx, UnitActionContext unitCtx) Build(
            Action<DataBinding<UnitData>>? configureUnit = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(4));
            var playerID = new PlayerID(Guid.NewGuid());
            var model = MakeModel(store, new Position(0, 0));
            var unit = MakeUnit(store, playerID, new[] { model });
            configureUnit?.Invoke(unit);

            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return (ctx, unitCtx);
        }

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: store);
            var modelRef = store.Create(model);
            return store.GetDataBinding<ModelData>(modelRef);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(playerID, "Test Unit", quality: 4, defense: 4,
                modelBindings: models.ToList());
            var unitRef = store.Create(unit);
            return store.GetDataBinding<UnitData>(unitRef);
        }
    }
}
