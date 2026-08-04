using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #330 — a match whose result can no longer change ends at that round's end instead of grinding out
    // the remaining rounds with only one player acting. Reported from play: a player who held every
    // objective and had tabled their opponent at the end of round 3 still had to hold every move through
    // round 4 to reach a foregone conclusion.
    //
    // The bar is zero false positives: every "does NOT end" test below is a match that is genuinely still
    // live, and ending it would be strictly worse than the original complaint. The off-table cases
    // (reserve / embarked / Reinforcement copy) are the whole risk surface, since a tabled-looking player
    // with a unit in reserve still has an army arriving.
    [TestFixture]
    public class MatchDecisionTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private PlayerID _alpha;
        private PlayerID _bravo;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _alpha = new PlayerID(Guid.NewGuid());
            _bravo = new PlayerID(Guid.NewGuid());
            CreatePlayer(_alpha, "Alpha", slotID: 0, teamNumber: 1);
            CreatePlayer(_bravo, "Bravo", slotID: 1, teamNumber: 2);
        }

        // --- Ends: the result is provably fixed -----------------------------------------------------

        [Test]
        public void TabledOpponentAndSoleLead_EndsTheMatch()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            CreateObjective(_alpha);
            CreateObjective(_alpha);
            CreateObjective(_bravo);

            Assert.That(IsFixed(out string headline, out string detail), Is.True,
                "the survivor's score can only rise and the tabled side's can only fall, so a sole lead is final.");
            Assert.That(headline, Is.EqualTo("No opposing forces remain"));
            Assert.That(detail, Does.Contain("Bravo"), "the log line names the side that can no longer act.");
        }

        [Test]
        public void ReportedCase_HoldsEveryObjectiveAndTabledOpponent_EndsTheMatch()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            CreateObjective(_alpha);
            CreateObjective(_alpha);
            CreateObjective(_alpha);

            Assert.That(IsFixed(out _, out _), Is.True);
        }

        [Test]
        public void EverySideWipedOut_EndsTheMatch()
        {
            Kill(CreateUnit(_alpha));
            Kill(CreateUnit(_bravo));
            CreateObjective(_alpha);
            CreateObjective(_bravo);

            // A frozen board: nobody can move, so no objective can change hands again. The standing
            // result here happens to be a tie, and a tie that can never be broken is still decided.
            Assert.That(IsFixed(out string headline, out string detail), Is.True);
            Assert.That(headline, Is.EqualTo("No forces remain on either side"));
            Assert.That(detail, Does.Contain("no living units left on any side"));
        }

        [Test]
        public void TabledTeammate_DoesNotKeepTheTeamAlive()
        {
            // #257 sides, not players: Bravo's whole TEAM must be dead, and a living ally keeps it in.
            var charlie = new PlayerID(Guid.NewGuid());
            CreatePlayer(charlie, "Charlie", slotID: 2, teamNumber: 2);
            _store.Create(new TeamData(1, new List<PlayerID> { _alpha }));
            _store.Create(new TeamData(2, new List<PlayerID> { _bravo, charlie }));

            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            Kill(CreateUnit(charlie));
            CreateObjective(_alpha);

            Assert.That(IsFixed(out _, out string detail), Is.True);
            Assert.That(detail, Does.Contain("Bravo and Charlie"),
                "both players on the eliminated side are named.");
        }

        // --- Does NOT end: the match is still live ---------------------------------------------------

        [Test]
        public void TabledOpponentButSurvivorIsBehind_DoesNotEnd()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            CreateObjective(_bravo);
            CreateObjective(_bravo);
            CreateObjective(_alpha);

            Assert.That(IsFixed(out _, out _), Is.False,
                "the survivor can still walk onto the markers and win - that game is worth playing out.");
        }

        [Test]
        public void TabledOpponentButScoresAreTied_DoesNotEnd()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            CreateObjective(_alpha);
            CreateObjective(_bravo);

            Assert.That(IsFixed(out _, out _), Is.False,
                "a tie the survivor could still break by seizing is not a decided match.");
        }

        [Test]
        public void TabledOpponentButNobodyHoldsAnything_DoesNotEnd()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            CreateObjective(owner: null);

            Assert.That(IsFixed(out _, out _), Is.False,
                "0-0 is not a sole lead; the survivor still has to go and take a marker.");
        }

        [Test]
        public void BothSidesStillHaveModels_DoesNotEnd()
        {
            CreateUnit(_alpha);
            CreateUnit(_bravo);
            CreateObjective(_alpha);
            CreateObjective(_alpha);
            CreateObjective(_alpha);

            Assert.That(IsFixed(out _, out _), Is.False,
                "a big lead is not a decided match while the enemy can still move.");
        }

        // --- Does NOT end: alive but off the table. This is the whole false-positive surface. --------

        [Test]
        public void OpponentUnitStillInReserve_DoesNotEnd()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            UnitData ambusher = CreateUnit(_bravo);
            ReserveRules.PlaceInReserve(ambusher);
            CreateObjective(_alpha);
            CreateObjective(_alpha);

            Assert.That(IsFixed(out _, out _), Is.False,
                "an Ambush unit held back is alive and off-table - it arrives next round and can seize.");
        }

        [Test]
        public void OpponentHasAPendingReinforcementCopy_DoesNotEnd()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));

            // What GameOperationServices.ReinforceUnit queues when a unit is destroyed: a full-strength
            // copy, alive, parked in reserve until the next round start places it.
            UnitData copy = CreateUnit(_bravo);
            ReserveRules.PlaceInReserve(copy);
            copy.Tokens.AddToken(TokenDefinitionCatalog.Create(
                Rules.Foundation.TokenType.PendingReinforcementArrival));
            CreateObjective(_alpha);
            CreateObjective(_alpha);

            Assert.That(IsFixed(out _, out _), Is.False,
                "a player can look tabled on the board and have a whole unit arriving next round.");
        }

        [Test]
        public void OpponentUnitFlewOffTheEdge_DoesNotEnd()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            UnitData aircraft = CreateUnit(_bravo);
            aircraft.Tokens.AddToken(TokenDefinitionCatalog.Create(
                Rules.Foundation.TokenType.OffTableFromForcedMove));
            CreateObjective(_alpha);
            CreateObjective(_alpha);

            Assert.That(IsFixed(out _, out _), Is.False,
                "an Aircraft that left the table comes back; it is alive and still in the game.");
        }

        // --- Shape of the output ---------------------------------------------------------------------

        [Test]
        public void SingleSidedTable_NeverEndsEarly()
        {
            // A solo scenario has no opponent being made to wait, and ending it at the first round end
            // would be a surprising change to those setups. Conservative on purpose.
            CreateUnit(_alpha);
            CreateObjective(_alpha);

            _store.Destroy(_store.GetAllDataReferences<PlayerSlotInfo>()
                .First(reference => _store.GetValue<PlayerSlotInfo>(reference).PlayerID.Equals(_bravo)));

            Assert.That(IsFixed(out _, out _), Is.False);
        }

        [Test]
        public void UndecidedMatch_ReturnsEmptyStrings()
        {
            CreateUnit(_alpha);
            CreateUnit(_bravo);

            Assert.That(IsFixed(out string headline, out string detail), Is.False);
            Assert.That(headline, Is.Empty);
            Assert.That(detail, Is.Empty);
        }

        [Test]
        public void AnnouncedTextIsAscii()
        {
            CreateUnit(_alpha);
            Kill(CreateUnit(_bravo));
            CreateObjective(_alpha);

            IsFixed(out string headline, out string detail);

            Assert.That(headline.All(c => c <= '\u007F'), Is.True, "game text is ASCII-only.");
            Assert.That(detail.All(c => c <= '\u007F'), Is.True, "game text is ASCII-only.");
        }

        [Test]
        public void EliminatedSideWithNoNamedSlot_IsDescribedGenerically()
        {
            var ghost = new PlayerID(Guid.NewGuid());
            CreateUnit(_alpha);
            Kill(CreateUnit(ghost));
            CreateObjective(_alpha);

            Assert.That(IsFixed(out _, out string detail), Is.True);
            Assert.That(detail, Does.Not.Contain(ghost.ID.ToString()),
                "an owner with no slot is described in words, never as a raw GUID.");
        }

        // --- The stage actually takes the early exit ------------------------------------------------

        [Test]
        public async Task Stage_DecidedAtRoundThree_TransitionsStraightToVictoryCalculation()
        {
            var log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), textOutput: log);
            CreateUnit(_alpha, near: new Position(50, 50));
            Kill(CreateUnit(_bravo));
            CreateObjective(_alpha);

            RecordingLayer layer = await RunReconcile(roundCount: 4);

            Assert.That(layer.Events, Is.EqualTo(new[]
            {
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION,
            }), "round 3 of 4 ends the game outright rather than starting another round.");
            Assert.That(log.Entries.Select(entry => entry.Message), Has.Some.Contains("Match decided after round 3 of 4"));
            Assert.That(log.Entries.Select(entry => entry.Message), Has.Some.Contains("No opposing forces remain - the match is decided."),
                "Announce writes the banner text to the log too, so both players get it.");
        }

        [Test]
        public async Task Stage_StillLiveAtRoundThree_StartsTheNextRoundAsBefore()
        {
            var log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), textOutput: log);
            CreateUnit(_alpha, near: new Position(50, 50));
            CreateUnit(_bravo, near: new Position(60, 60));
            CreateObjective(_alpha);

            RecordingLayer layer = await RunReconcile(roundCount: 4);

            Assert.That(layer.Events, Is.EqualTo(new[]
            {
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN,
            }), "with both sides alive the round loop is untouched.");
            Assert.That(log.Entries.Select(entry => entry.Message), Has.None.Contains("Match decided"));
        }

        [Test]
        public async Task Stage_FinalRound_StillEndsOnTheRoundLimit()
        {
            var log = new RecordingTextOutput();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), textOutput: log);
            CreateUnit(_alpha, near: new Position(50, 50));
            CreateUnit(_bravo, near: new Position(60, 60));
            CreateObjective(_alpha);

            RecordingLayer layer = await RunReconcile(roundCount: GameWideConstants.NUMBER_OF_ROUNDS + 1);

            Assert.That(layer.Events, Is.EqualTo(new[]
            {
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION,
            }));
            Assert.That(log.Entries.Select(entry => entry.Message), Has.Some.Contains("rounds complete"),
                "the ordinary round-limit ending (#195) is unchanged, and does not claim to be an early call.");
            Assert.That(log.Entries.Select(entry => entry.Message), Has.None.Contains("Match decided"));
        }

        // Helpers

        private async Task<RecordingLayer> RunReconcile(int roundCount)
        {
            var layer = new RecordingLayer();
            var stage = new ReconcileObjectivesStage(_ctx, layer);
            stage.ToReconcileEndOfTurn.Bind(
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN);
            stage.ToVictoryCalculation.Bind(
                ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION);
            await stage.Enter(new RoundedMainPhaseContext(_ctx, roundCount));
            return layer;
        }

        private bool IsFixed(out string headline, out string detail) =>
            MatchDecision.IsResultFixed(_ctx.TableState, out headline, out detail);

        private void CreatePlayer(PlayerID playerID, string name, int slotID, int teamNumber) =>
            _store.Create(new PlayerSlotInfo(playerID, slotID: slotID, teamNumber: teamNumber,
                name: name, isFilled: true));

        private void CreateObjective(PlayerID? owner)
        {
            var objective = new ObjectiveData(new Position(0, 0), _store);
            _store.Create(objective);
            if (owner.HasValue) objective.SetOwner(owner.Value);
        }

        // `near` matters only for the stage tests, where a model parked on an objective would seize it and
        // change the very score the decision reads; the pure-helper tests set ownership directly.
        private UnitData CreateUnit(PlayerID playerID, Position? near = null)
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: near ?? new Position(0, 0), gameDataStore: _store);
            DataReference modelReference = _store.Create(model);
            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    _store.GetDataBinding<ModelData>(modelReference),
                });
            _store.Create(unit);
            return unit;
        }

        private static void Kill(UnitData unit)
        {
            foreach (IModel model in unit.Models)
                ((ModelData)model).DealWounds(((ModelData)model).TotalWounds);
        }
    }

    // Parent layer that records which transition a stage took, in order.
    internal sealed class RecordingLayer : IStateMachineLayer<IMainPhaseContext>
    {
        public List<string> Events { get; } = new List<string>();

        public Task ExecuteTransition(string eventName, StageBase<IMainPhaseContext> leaving,
            IMainPhaseContext context)
        {
            Events.Add(eventName);
            return Task.CompletedTask;
        }

        public void NotifyChildEntered(IStage stage) { }
        public void NotifyChildExited(IStage stage) { }
    }

    // StubMainPhaseContext with a settable round number: ReconcileObjectivesStage reads RoundCount - 1 as
    // the round that just finished (#195), so this is what decides which branch it is being asked about.
    internal sealed class RoundedMainPhaseContext : IMainPhaseContext
    {
        public IGameContext GameContext { get; }
        public int RoundCount { get; }
        public List<ITeam> TeamActivateOrder => new List<ITeam>();

        public RoundedMainPhaseContext(IGameContext gameContext, int roundCount)
        {
            GameContext = gameContext;
            RoundCount = roundCount;
        }

        public void OnEndOfRound(IReadOnlyList<ITeam> newTeamActivateOrder) { }
    }
}
