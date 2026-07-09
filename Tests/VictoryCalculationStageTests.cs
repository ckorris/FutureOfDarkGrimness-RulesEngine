using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #064 — VictoryCalculationStage was implemented (objective tally → winner/tie) but fully
    // untested. These pin the end-of-game contract: the exact string handed to NotifyGameCompleted,
    // which propagates GameContext.OnGameCompleted → FDGServer → CliApp and decides the match.
    // #192 — the same call now carries a structured GameResult (outcome / winner / scores / rounds);
    // automated play reads that instead of parsing the prose, so both halves are pinned here.
    [TestFixture]
    public class VictoryCalculationStageTests
    {
        private GameDataStore _store = null!;
        private RecordingGameEndContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new RecordingGameEndContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public async Task NoObjectives_EndsInTie()
        {
            await RunVictory();

            Assert.That(_ctx.EndResult, Is.EqualTo("It's a tie!"));
            Assert.That(_ctx.Result!.Outcome, Is.EqualTo(EGameOutcome.Tie));
            Assert.That(_ctx.Result.Winner, Is.Null);
        }

        [Test]
        public async Task ObjectivesExistButNoneOwned_EndsInTie()
        {
            CreateObjective(owner: null);
            CreateObjective(owner: null);

            await RunVictory();

            Assert.That(_ctx.EndResult, Is.EqualTo("It's a tie!"),
                "objectives with no owner contribute nothing to any player's score.");
            Assert.That(_ctx.Result!.Outcome, Is.EqualTo(EGameOutcome.Tie));
        }

        [Test]
        public async Task SinglePlayerControlsMost_ThatPlayerWins()
        {
            var winner = new PlayerID(Guid.NewGuid());
            var loser = new PlayerID(Guid.NewGuid());
            CreatePlayer(winner, "Crimson Fists", slotID: 0);
            CreateObjective(owner: winner);
            CreateObjective(owner: winner);
            CreateObjective(owner: loser);

            await RunVictory();

            // The result string uses the player's display name (matching the on-screen banner), not the
            // raw PlayerID GUID — see work item #040.
            Assert.That(_ctx.EndResult, Is.EqualTo("Crimson Fists wins!"));
            Assert.That(_ctx.Result!.Outcome, Is.EqualTo(EGameOutcome.Win));
            Assert.That(_ctx.Result.Winner, Is.EqualTo(winner));
            Assert.That(_ctx.Result.WinnerName, Is.EqualTo("Crimson Fists"));
        }

        [Test]
        public async Task WinnerNotInPlayerList_FallsBackToGenericName()
        {
            var winner = new PlayerID(Guid.NewGuid());
            CreateObjective(owner: winner);

            await RunVictory();

            Assert.That(_ctx.EndResult, Is.EqualTo("A player wins!"),
                "with no matching player slot, the name resolves to the 'A player' fallback, never a GUID.");
            Assert.That(_ctx.Result!.Winner, Is.EqualTo(winner),
                "the structured winner is the real PlayerID even when no slot supplies a display name.");
        }

        [Test]
        public async Task TwoPlayersTiedAtTopScore_EndsInTie()
        {
            var playerA = new PlayerID(Guid.NewGuid());
            var playerB = new PlayerID(Guid.NewGuid());
            CreateObjective(owner: playerA);
            CreateObjective(owner: playerB);

            await RunVictory();

            Assert.That(_ctx.EndResult, Is.EqualTo("It's a tie!"),
                "a two-way tie at the top score is a tie, not a win for either.");
            Assert.That(_ctx.Result!.Outcome, Is.EqualTo(EGameOutcome.Tie));
            Assert.That(_ctx.Result.Winner, Is.Null);
        }

        // #192 structured-result facets.

        [Test]
        public async Task Result_CarriesFinalScoresInSlotOrder()
        {
            var playerA = new PlayerID(Guid.NewGuid());
            var playerB = new PlayerID(Guid.NewGuid());
            CreatePlayer(playerA, "Alpha", slotID: 0);
            CreatePlayer(playerB, "Beta", slotID: 1);
            CreateObjective(owner: playerA);
            CreateObjective(owner: playerA);
            CreateObjective(owner: playerB);
            CreateObjective(owner: null);

            await RunVictory();

            Assert.That(_ctx.Result!.Scores.Select(s => s.PlayerID),
                Is.EqualTo(new[] { playerA, playerB }), "scores are per filled slot, in slot order.");
            Assert.That(_ctx.Result.Scores.Select(s => s.ObjectiveCount),
                Is.EqualTo(new[] { 2, 1 }), "the unowned objective counts for nobody.");
        }

        [Test]
        public async Task Result_WinnerMatchesTopOfScoreTally()
        {
            var playerA = new PlayerID(Guid.NewGuid());
            var playerB = new PlayerID(Guid.NewGuid());
            CreatePlayer(playerA, "Alpha", slotID: 0);
            CreatePlayer(playerB, "Beta", slotID: 1);
            CreateObjective(owner: playerA);
            CreateObjective(owner: playerB);
            CreateObjective(owner: playerB);

            await RunVictory();

            // The engine invariant: objectives decide the winner. Assert the declared winner really is the
            // player with the strictly highest objective count in the same result's own score list.
            var top = _ctx.Result!.Scores.OrderByDescending(s => s.ObjectiveCount).First();
            Assert.That(_ctx.Result.Outcome, Is.EqualTo(EGameOutcome.Win));
            Assert.That(_ctx.Result.Winner, Is.EqualTo(top.PlayerID));
            Assert.That(_ctx.Result.WinnerName, Is.EqualTo("Beta"));
        }

        [Test]
        public async Task Result_RoundsPlayedReadsProgress()
        {
            CreateProgress(roundCount: 4);
            CreateObjective(owner: null);

            await RunVictory();

            Assert.That(_ctx.Result!.RoundsPlayed, Is.EqualTo(4));
        }

        [Test]
        public async Task Result_RoundsPlayedIsZeroWithoutProgressRecord()
        {
            await RunVictory();

            Assert.That(_ctx.Result!.RoundsPlayed, Is.EqualTo(0),
                "a game that never reached the main phase has no progress record to read.");
        }

        [Test]
        public async Task Result_SummaryLineIsStableAndAscii()
        {
            var winner = new PlayerID(Guid.NewGuid());
            CreatePlayer(winner, "Crimson Fists", slotID: 0);
            CreateProgress(roundCount: 4);
            CreateObjective(owner: winner);

            await RunVictory();

            string line = _ctx.Result!.ToSummaryLine();
            Assert.That(line, Is.EqualTo("outcome=Win winner=\"Crimson Fists\" rounds=4 scores=[1]"));
            Assert.That(line, Does.Not.Contain("\n"), "the summary must stay a single greppable line.");
            Assert.That(line.All(c => c <= '\u007F'), Is.True, "game text is ASCII-only.");
        }

        [Test]
        public void ForFault_IsALossForNobodyWithNoScores()
        {
            GameResult fault = GameResult.ForFault("Player 2 left the game.");

            Assert.That(fault.Outcome, Is.EqualTo(EGameOutcome.Fault));
            Assert.That(fault.Winner, Is.Null);
            Assert.That(fault.Scores, Is.Empty);
            Assert.That(fault.RoundsPlayed, Is.EqualTo(0));
            Assert.That(fault.Message, Is.EqualTo("Player 2 left the game."));
        }

        // Helpers

        private Task RunVictory()
        {
            var stage = new VictoryCalculationStage(_ctx, new NoOpLayer<IGameContext>());
            return stage.Enter(_ctx);
        }

        private void CreateObjective(PlayerID? owner)
        {
            var obj = new ObjectiveData(new Position(0, 0), _store);
            _store.Create(obj);
            if (owner.HasValue)
                obj.SetOwner(owner.Value);
        }

        private void CreatePlayer(PlayerID playerID, string name, int slotID)
        {
            _store.Create(new PlayerSlotInfo(playerID, slotID: slotID, teamNumber: 0, name: name, isFilled: true));
        }

        private void CreateProgress(int roundCount)
        {
            _store.Create(new GameProgressData(
                stage: EResumeStage.MainPhase,
                roundCount: roundCount,
                teamActivateOrder: new List<int>(),
                currentRoundTeamFinishOrder: new List<int>(),
                currentTeamIndex: 0,
                currentPlayerIndexPerTeam: new Dictionary<int, int>(),
                unactivatedUnits: new List<DataBinding<UnitData>>(),
                settings: GameSettings.GetDefault()));
        }
    }

    // Captures the single NotifyGameCompleted call VictoryCalculationStage makes. EndResult keeps the
    // pre-#192 string assertions honest; Result pins the structured record.
    internal sealed class RecordingGameEndContext : TestGameContext
    {
        public GameResult? Result { get; private set; }
        public string? EndResult => Result?.Message;

        public RecordingGameEndContext(GameDataStore store, IDiceRoller diceRoller)
            : base(store, diceRoller) { }

        public override void NotifyGameCompleted(GameResult result) => Result = result;
    }
}
