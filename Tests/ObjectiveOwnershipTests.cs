using FDG.Data;
using FDG.Rules.Dispatch;
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

        // A unit that arrived from reserve (Ambush) this round carries the ArrivedFromReserve marker; it
        // can neither seize nor contest objectives that round, so an objective it sits alone on stays as-is.
        [Test]
        public async Task ReconcileObjectivesStage_ArrivedThisRoundUnit_DoesNotSeize()
        {
            var objective = CreateObjective(new Position(5, 5));
            UnitData arrived = CreateUnit(new PlayerID(Guid.NewGuid()), modelPosition: new Position(5, 5));
            MarkArrivedThisRound(arrived);

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.Null,
                "a unit that arrived from reserve this round cannot seize the objective it stands on.");
        }

        // The newcomer doesn't contest either: an enemy sharing the objective seizes it outright, as if
        // the arrived unit weren't there.
        [Test]
        public async Task ReconcileObjectivesStage_ArrivedUnitDoesNotContest_EnemySeizes()
        {
            var objective = CreateObjective(new Position(5, 5));
            var enemy = new PlayerID(Guid.NewGuid());
            UnitData arrived = CreateUnit(new PlayerID(Guid.NewGuid()), modelPosition: new Position(5.0f, 5));
            MarkArrivedThisRound(arrived);
            CreateUnit(enemy, modelPosition: new Position(5.5f, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(enemy),
                "the arrived unit can't contest, so the enemy seizes uncontested rather than the objective going neutral.");
        }

        // The exclusion lasts exactly one round: the marker is cleared at the end of the reconcile that
        // read it, so on the next round's reconcile the unit seizes normally.
        [Test]
        public async Task ReconcileObjectivesStage_ArrivalMarkerClearsAfterCheck_SeizesNextRound()
        {
            var playerID = new PlayerID(Guid.NewGuid());
            var objective = CreateObjective(new Position(5, 5));
            UnitData arrived = CreateUnit(playerID, modelPosition: new Position(5, 5));
            MarkArrivedThisRound(arrived);

            await RunReconcileOnce(); // round of arrival — excluded
            Assert.That(objective.OwnerID, Is.Null, "still excluded the round it arrives.");

            await RunReconcileOnce(); // next round — marker already cleared
            Assert.That(objective.OwnerID, Is.EqualTo(playerID),
                "the RoundEnd marker was swept after the first check, so the unit seizes the following round.");
        }

        // #029 — an Aircraft can neither seize nor contest objectives, even sitting alone on one.
        [Test]
        public async Task ReconcileObjectivesStage_AircraftAlone_DoesNotSeize()
        {
            var objective = CreateObjective(new Position(5, 5));
            UnitData aircraft = CreateUnit(new PlayerID(Guid.NewGuid()), modelPosition: new Position(5, 5));
            aircraft.AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.Null, "an Aircraft can't seize the objective it sits on.");
        }

        [Test]
        public async Task ReconcileObjectivesStage_AircraftDoesNotContest_GroundEnemySeizes()
        {
            var objective = CreateObjective(new Position(5, 5));
            var enemy = new PlayerID(Guid.NewGuid());
            UnitData aircraft = CreateUnit(new PlayerID(Guid.NewGuid()), modelPosition: new Position(5.0f, 5));
            aircraft.AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));
            CreateUnit(enemy, modelPosition: new Position(5.5f, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(enemy),
                "the Aircraft can't contest, so the ground enemy seizes uncontested.");
        }

        // Helpers

        private static void MarkArrivedThisRound(UnitData unit) =>
            unit.Tokens.AddToken(new Rules.Tokens.Token(Rules.Foundation.TokenType.ArrivedFromReserve, 1,
                new Rules.Foundation.TokenClearTrigger.RoundEnd()));

        private ObjectiveData CreateObjective(Position position)
        {
            var obj = new ObjectiveData(position, _store);
            _store.Create(obj);
            return obj;
        }

        private UnitData CreateUnit(PlayerID playerID, Position modelPosition)
        {
            var modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: modelPosition,
                gameDataStore: _store);
            var modelRef = _store.Create(modelData);
            var modelBinding = _store.GetDataBinding<ModelData>(modelRef);

            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            _store.Create(unit);
            return unit;
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
