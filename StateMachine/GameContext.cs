using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.TempVisuals;

namespace FDG
{
    /// <summary>
    /// References to required objects for running the state machine.
    /// <para>This is _not_ meant to be serialized.</para>
    /// </summary>
    public interface IGameContext : IGameContextAccessor
    {
        public ITextOutput TextOutput { get; }

        public IDiceRoller DiceRoller { get; }

        public RuleEvaluator RuleEvaluator { get; }
        
        public IPlayerRequestByID PlayerRequester { get; }

        public TableState TableState { get; }

        //TODO: Maybe move these from being accessible, if the engine layer
        //is to have access to it (I'm undecided). But that would
        //mean redoing lots of stages, and I'm on an airplane as I type this.
        public IReadWriteableGameDataStore GameDataStore { get; }

        public ITempVisualDrawer TempVisualDrawer { get; }

        public GameSettings Settings { get; }

        public List<ITeam>? FirstDeploymentRollOrder { get; }

        public void SetFirstDeploymentRollOrder(List<ITeam> firstDeploymentRollWinner);

        public void NotifyGameEnded(string result);
    }

    public class GameContext : IGameContext
    {
        IGameContext IGameContextAccessor.GameContext => this;

        public ITextOutput TextOutput { get; }

        public IDiceRoller DiceRoller { get; }

        public RuleEvaluator RuleEvaluator { get; }
        
        public IPlayerRequestByID PlayerRequester { get; }

        public TableState TableState { get; }

        public IReadWriteableGameDataStore GameDataStore { get; }

        public ITempVisualDrawer TempVisualDrawer { get; }

        public GameSettings Settings { get; }

        public List<ITeam>? FirstDeploymentRollOrder { get; private set; } = null;

        public event Action<string>? OnGameEnded;

        public GameContext(ITextOutput textOutput, IDiceRoller diceRoller,
                IPlayerRequestByID playerRequester,
                TableState tableState,
                IReadWriteableGameDataStore gameDataStore,
                ITempVisualDrawer tempVisualDrawer,
                GameSettings settings)
        {
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            RuleEvaluator = new RuleEvaluator(diceRoller, textOutput);
            PlayerRequester = playerRequester;
            TableState = tableState;
            GameDataStore = gameDataStore;
            TempVisualDrawer = tempVisualDrawer;
            Settings = settings;
        }

        public void SetFirstDeploymentRollOrder(List<ITeam> firstDeploymentRollWinner)
        {
            if (FirstDeploymentRollOrder != null)
            {
                throw new InvalidOperationException($"Tried to set roll winner order, but it was already set.");
            }

            FirstDeploymentRollOrder = firstDeploymentRollWinner;
        }

        public void NotifyGameEnded(string result)
        {
            OnGameEnded?.Invoke(result);
        }
    }
}