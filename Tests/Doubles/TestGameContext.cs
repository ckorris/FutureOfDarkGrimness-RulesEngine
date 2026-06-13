using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Dispatch;

namespace FDG.Tests
{
    // Minimal IGameContext backed by real GameDataStore and TableState.
    internal class TestGameContext : IGameContext
    {
        public ITextOutput TextOutput { get; }
        public IDiceRoller DiceRoller { get; }
        public RuleEvaluator RuleEvaluator { get; }
        public IPlayerRequestByID PlayerRequester { get; } = new NullPlayerRequester();
        public TableState TableState { get; }
        public IReadWriteableGameDataStore GameDataStore { get; }
        public IPresenter Presenter { get; }
        public GameSettings Settings { get; } = GameSettings.GetDefault();
        public List<ITeam>? FirstDeploymentRollOrder => null;
        IGameContext IGameContextAccessor.GameContext => this;

        // textOutput/presenter are injectable so tests can assert logging / beats; they default to
        // a no-op log and an instant, sink-less presenter (beats paced instantly and dropped) so
        // tests that don't care about presentation are unaffected.
        public TestGameContext(GameDataStore store, IDiceRoller diceRoller,
            ITextOutput? textOutput = null, IPresenter? presenter = null)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            DiceRoller = diceRoller;
            RuleEvaluator = new RuleEvaluator(diceRoller);
            TextOutput = textOutput ?? new EmptyTextOutput();
            Presenter = presenter ?? new LocalPresenter(sink: null, new InstantPresentationClock());
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> order) { }

        // Virtual so tests that care about the end-of-game contract (e.g. VictoryCalculationStage)
        // can subclass and record the result string; the base is a no-op for everyone else.
        public virtual void NotifyGameEnded(string result) { }
    }
}
