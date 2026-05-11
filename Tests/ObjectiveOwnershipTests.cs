using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class ObjectiveOwnershipTests
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
        public void SetOwner_WhenCalled_FiresDataUpdatedEvent()
        {
            var objective = CreateObjective(new Position(5, 5));

            bool eventFired = false;
            _store.OnDataUpdatedAsJson += (_, _) => eventFired = true;

            objective.SetOwner(new PlayerID(Guid.NewGuid()));

            Assert.That(eventFired, Is.True);
        }

        [Test]
        public async Task ReconcileObjectivesStage_SinglePlayerNearby_AssignsOwner()
        {
            var playerID = new PlayerID(Guid.NewGuid());
            var objective = CreateObjective(new Position(5, 5));
            CreateUnit(playerID, modelPosition: new Position(5, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(playerID));
        }

        [Test]
        public async Task ReconcileObjectivesStage_NoPlayersNearby_OwnerRemainsNull()
        {
            var objective = CreateObjective(new Position(5, 5));
            CreateUnit(new PlayerID(Guid.NewGuid()), modelPosition: new Position(20, 20));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.Null);
        }

        [Test]
        public async Task ReconcileObjectivesStage_TwoPlayersNearby_OwnerBecomesNull()
        {
            var objective = CreateObjective(new Position(5, 5));
            objective.SetOwner(new PlayerID(Guid.NewGuid())); // previously captured

            CreateUnit(new PlayerID(Guid.NewGuid()), modelPosition: new Position(5.0f, 5));
            CreateUnit(new PlayerID(Guid.NewGuid()), modelPosition: new Position(5.5f, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.Null);
        }

        // Helpers

        private ObjectiveData CreateObjective(Position position)
        {
            var obj = new ObjectiveData(position, _store);
            _store.Create(obj);
            return obj;
        }

        private void CreateUnit(PlayerID playerID, Position modelPosition)
        {
            var modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                specialRules: new List<SpecialRule>(),
                initialPosition: modelPosition,
                gameDataStore: _store);
            var modelRef = _store.Create(modelData);
            var modelBinding = _store.GetDataBinding<ModelData>(modelRef);

            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            _store.Create(unit);
        }

        private Task RunReconcileOnce()
        {
            var stage = new ReconcileObjectivesStage(_ctx, new NoOpLayer<IMainPhaseContext>());
            stage.ToReconcileEndOfTurn.Bind(
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN);
            stage.ToVictoryCalculation.Bind(
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION);
            return stage.Enter(new StubMainPhaseContext(_ctx));
        }
    }

    internal class StubMainPhaseContext : IMainPhaseContext
    {
        public IGameContext GameContext { get; }
        public int RoundCount => 1;
        public List<ITeam> TeamActivateOrder => new List<ITeam>();
        public void OnEndOfRound(IReadOnlyList<ITeam> newTeamActivateOrder) { }
        public StubMainPhaseContext(IGameContext ctx) => GameContext = ctx;
    }
}
