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

        // --- #297: objectives are held per SIDE - allied players guarding one marker do not
        // contest it to neutral (victory pools per team, #257; the old per-player rule had two
        // teammates on a marker un-score it for their own side).

        [Test]
        public async Task ReconcileObjectivesStage_TwoAlliedPlayersNearby_TeamHoldsMarker()
        {
            var a = new PlayerID(Guid.NewGuid());
            var b = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { a, b }));
            var objective = CreateObjective(new Position(5, 5));
            CreateUnit(a, modelPosition: new Position(5.0f, 5));
            CreateUnit(b, modelPosition: new Position(5.5f, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(a),
                "allied players sharing a marker hold it for their side (first-registered in range), not neutral.");
        }

        [Test]
        public async Task ReconcileObjectivesStage_AlliedPlayersNearby_CurrentOwnerOnSideKeepsMarker()
        {
            var a = new PlayerID(Guid.NewGuid());
            var b = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { a, b }));
            var objective = CreateObjective(new Position(5, 5));
            objective.SetOwner(b); // b seized it earlier; a walks up alongside
            CreateUnit(a, modelPosition: new Position(5.0f, 5));
            CreateUnit(b, modelPosition: new Position(5.5f, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(b),
                "ownership is sticky within the side - the original seizer keeps the marker.");
        }

        [Test]
        public async Task ReconcileObjectivesStage_AlliedAndEnemyNearby_BecomesNeutral()
        {
            var a = new PlayerID(Guid.NewGuid());
            var b = new PlayerID(Guid.NewGuid());
            var enemy = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { a, b }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemy }));
            var objective = CreateObjective(new Position(5, 5));
            CreateUnit(a, modelPosition: new Position(5.0f, 5));
            CreateUnit(b, modelPosition: new Position(5.5f, 5));
            CreateUnit(enemy, modelPosition: new Position(4.5f, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.Null,
                "opposing sides on the same marker still contest it to neutral.");
        }

        [Test]
        public async Task ReconcileObjectivesStage_TeammateGuardsAbsentOwnersMarker_OwnerKeepsIt()
        {
            var a = new PlayerID(Guid.NewGuid());
            var b = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { a, b }));
            var objective = CreateObjective(new Position(5, 5));
            objective.SetOwner(a); // a seized it, then moved on; b stands guard
            CreateUnit(b, modelPosition: new Position(5.0f, 5));
            CreateUnit(a, modelPosition: new Position(30, 30));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(a),
                "a teammate guarding the marker keeps it with its original owner.");
        }

        // Helpers

        private static void MarkArrivedThisRound(UnitData unit) =>
            unit.Tokens.AddToken(new Rules.Tokens.Token(Rules.Foundation.TokenType.ArrivedFromReserve, 1,
                new Rules.Foundation.TokenClearTrigger.RoundEnd()));

        [Test]
        public async Task ReconcileObjectivesStage_RectangularBase_LongAxisTowardObjective_Seizes()
        {
            // A 1"×6" base centred 4" from the objective along Z, facing +Z so its long (6") axis points at the
            // objective: the base edge is 1" away — inside the 3" seizure range. The true footprint + facing,
            // not the (inscribed-circle) bounding radius, decides this (#150).
            var playerID = new PlayerID(Guid.NewGuid());
            var objective = CreateObjective(new Position(5, 5));
            CreateUnit(playerID, new RectangleBase(1f, 6f), new Position(5, 9), new Float2(0f, 1f));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(playerID), "long axis toward the objective (edge 1\" away) seizes.");
        }

        [Test]
        public async Task ReconcileObjectivesStage_RectangularBase_ShortAxisTowardObjective_DoesNotSeize()
        {
            // Same 1"×6" base and position, rotated to face +X so only its 1"-wide axis points at the objective:
            // the base edge is 3.5" away — outside the 3" range. Rotating the base alone flips the outcome (#150).
            var objective = CreateObjective(new Position(5, 5));
            CreateUnit(new PlayerID(Guid.NewGuid()), new RectangleBase(1f, 6f), new Position(5, 9), new Float2(1f, 0f));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.Null, "short axis toward the objective (edge 3.5\" away) does not seize.");
        }

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

        // Overload with an explicit base shape + facing, for the #150 orientation-aware seizure tests.
        private UnitData CreateUnit(PlayerID playerID, IBaseShape shape, Position modelPosition, Float2 facing)
        {
            var modelData = new ModelData(shape, new List<Weapon>(), modelPosition, _store);
            modelData.SetFacing(facing);
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
