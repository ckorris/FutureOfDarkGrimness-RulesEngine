using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.TempVisuals;

namespace FDG.Tests
{
    // Minimal IGameContext backed by real GameDataStore and TableState.
    internal class TestGameContext : IGameContext
    {
        public ITextOutput TextOutput { get; } = new EmptyTextOutput();
        public IDiceRoller DiceRoller { get; }
        public RuleEvaluator RuleEvaluator { get; }
        public IPlayerRequestByID PlayerRequester { get; } = new NullPlayerRequester();
        public TableState TableState { get; }
        public IReadWriteableGameDataStore GameDataStore { get; }
        public ITempVisualDrawer TempVisualDrawer { get; } = new NullTempVisualDrawer();
        public GameSettings Settings { get; } = GameSettings.GetDefault();
        public List<ITeam>? FirstDeploymentRollOrder => null;
        IGameContext IGameContextAccessor.GameContext => this;

        public TestGameContext(GameDataStore store, IDiceRoller diceRoller)
        {
            GameDataStore = store;
            TableState = new TableState(store);
            DiceRoller = diceRoller;
            RuleEvaluator = new RuleEvaluator(diceRoller);
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> order) { }
        public void NotifyGameEnded(string result) { }
    }
}
