using FDG.Data;
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

        private static (TestGameContext ctx, UnitActionContext unitCtx) Build()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(4));
            var playerID = new PlayerID(Guid.NewGuid());
            var model = MakeModel(store, new Position(0, 0));
            var unit = MakeUnit(store, playerID, new[] { model });

            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return (ctx, unitCtx);
        }

        private static DataBinding<ModelData> MakeModel(GameDataStore store, Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                specialRules: new List<SpecialRule>(),
                initialPosition: position,
                gameDataStore: store);
            var modelRef = store.Create(model);
            return store.GetDataBinding<ModelData>(modelRef);
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID playerID,
            IEnumerable<DataBinding<ModelData>> models)
        {
            var unit = new UnitData(playerID, "Test Unit", quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: models.ToList());
            var unitRef = store.Create(unit);
            return store.GetDataBinding<UnitData>(unitRef);
        }
    }
}
