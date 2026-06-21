using FDG.Data;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #064 — VictoryCalculationStage was implemented (objective tally → winner/tie) but fully
    // untested. These pin the end-of-game contract: the exact string handed to NotifyGameEnded,
    // which propagates GameContext.OnGameEnded → FDGServer → CliApp and decides the match.
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
        }

        [Test]
        public async Task ObjectivesExistButNoneOwned_EndsInTie()
        {
            CreateObjective(owner: null);
            CreateObjective(owner: null);

            await RunVictory();

            Assert.That(_ctx.EndResult, Is.EqualTo("It's a tie!"),
                "objectives with no owner contribute nothing to any player's score.");
        }

        [Test]
        public async Task SinglePlayerControlsMost_ThatPlayerWins()
        {
            var winner = new PlayerID(Guid.NewGuid());
            var loser = new PlayerID(Guid.NewGuid());
            CreatePlayer(winner, "Crimson Fists");
            CreateObjective(owner: winner);
            CreateObjective(owner: winner);
            CreateObjective(owner: loser);

            await RunVictory();

            // The result string uses the player's display name (matching the on-screen banner), not the
            // raw PlayerID GUID — see work item #040.
            Assert.That(_ctx.EndResult, Is.EqualTo("Crimson Fists wins!"));
        }

        [Test]
        public async Task WinnerNotInPlayerList_FallsBackToGenericName()
        {
            var winner = new PlayerID(Guid.NewGuid());
            CreateObjective(owner: winner);

            await RunVictory();

            Assert.That(_ctx.EndResult, Is.EqualTo("A player wins!"),
                "with no matching player slot, the name resolves to the 'A player' fallback, never a GUID.");
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

        private void CreatePlayer(PlayerID playerID, string name)
        {
            _store.Create(new PlayerSlotInfo(playerID, slotID: 0, teamNumber: 0, name: name, isFilled: true));
        }
    }

    // Captures the single NotifyGameEnded call VictoryCalculationStage makes.
    internal sealed class RecordingGameEndContext : TestGameContext
    {
        public string? EndResult { get; private set; }

        public RecordingGameEndContext(GameDataStore store, IDiceRoller diceRoller)
            : base(store, diceRoller) { }

        public override void NotifyGameEnded(string result) => EndResult = result;
    }
}
