using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Dispatch;

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

        /// <summary>
        /// The game's single seeded source of NON-dice randomness (shuffles, tie-breaks that aren't rolls).
        /// Dice go through <see cref="DiceRoller"/>. Nothing on the game path may construct its own
        /// <c>Random</c>: same seed + same build must reproduce the same game, including when many games
        /// run concurrently in one process (#193). Seeded from <see cref="GameSettings.DiceSeed"/>.
        /// </summary>
        public Random Rng { get; }

        public RuleEvaluator RuleEvaluator { get; }

        public IPlayerRequestByID PlayerRequester { get; }

        public TableState TableState { get; }

        //TODO: Maybe move these from being accessible, if the engine layer
        //is to have access to it (I'm undecided). But that would
        //mean redoing lots of stages, and I'm on an airplane as I type this.
        public IReadWriteableGameDataStore GameDataStore { get; }

        /// <summary>
        /// Outbound presentation channel. Stages emit paced, semantic beats here
        /// (<c>await context.Presenter.Present(beat)</c>) to narrate the play-by-play. See
        /// <see cref="IPresenter"/>.
        /// </summary>
        public IPresenter Presenter { get; }

        public GameSettings Settings { get; }

        public List<ITeam>? FirstDeploymentRollOrder { get; }

        public void SetFirstDeploymentRollOrder(List<ITeam> firstDeploymentRollWinner);

        /// <summary>
        /// Ends the game with a structured <see cref="GameResult"/>. Raised once, by
        /// <see cref="Stages.VictoryCalculationStage"/>. <see cref="GameModel.FDGServer"/> fans this out to
        /// both its structured <c>OnGameCompleted</c> event and its legacy <c>OnGameEnded(string)</c> event.
        /// </summary>
        public void NotifyGameCompleted(GameResult result);

        /// <summary>
        /// When resuming a loaded game, the flow-state snapshot to rebuild the first round from;
        /// null for a fresh game. Consumed once (see <see cref="ConsumeResumeProgress"/>) so later
        /// rounds run normally. Defaulted so non-resuming contexts (e.g. tests) needn't implement it.
        /// </summary>
        public GameProgressData? ResumeProgress => null;

        /// <summary>Clears <see cref="ResumeProgress"/> after the resumed round has been rebuilt.</summary>
        public void ConsumeResumeProgress() { }

        /// <summary>
        /// #191 B1 5c (D10a): the simulation pause/step seam, called at every activation boundary.
        /// <b>Null in all real play</b> - only <see cref="Simulation.SimulationService"/> sets it,
        /// so a normal game never makes this call. Defaulted so no other context implements it.
        /// </summary>
        public Simulation.IActivationBoundaryHook? ActivationBoundaryHook => null;
    }

    public class GameContext : IGameContext
    {
        IGameContext IGameContextAccessor.GameContext => this;

        public ITextOutput TextOutput { get; }

        public IDiceRoller DiceRoller { get; }

        public Random Rng { get; }

        public RuleEvaluator RuleEvaluator { get; }

        public IPlayerRequestByID PlayerRequester { get; }

        public TableState TableState { get; }

        public IReadWriteableGameDataStore GameDataStore { get; }

        public IPresenter Presenter { get; }

        public GameSettings Settings { get; }

        public List<ITeam>? FirstDeploymentRollOrder { get; private set; } = null;

        public GameProgressData? ResumeProgress { get; private set; }

        /// <inheritdoc />
        public Simulation.IActivationBoundaryHook? ActivationBoundaryHook { get; }

        public event Action<GameResult>? OnGameCompleted;

        public GameContext(ITextOutput textOutput, IDiceRoller diceRoller,
                IPlayerRequestByID playerRequester,
                TableState tableState,
                IReadWriteableGameDataStore gameDataStore,
                IPresenter presenter,
                GameSettings settings,
                GameProgressData? resumeProgress = null,
                IRuleResolver? ruleResolver = null,
                Simulation.IActivationBoundaryHook? activationBoundaryHook = null)
        {
            ActivationBoundaryHook = activationBoundaryHook;
            TextOutput = textOutput;
            DiceRoller = diceRoller;
            Rng = GameRandom.Create(settings.DiceSeed, GameRandom.SALT_GAME_CONTEXT);
            // ruleResolver lets the evaluator read token-granted rules (auras / "gains rule X" buffs)
            // back to their definitions. Optional so the Samples' bare-context construction and the
            // resume path (no fresh army-load resolver; see #095) still compile and run.
            RuleEvaluator = new RuleEvaluator(diceRoller, textOutput, ruleResolver);
            PlayerRequester = playerRequester;
            TableState = tableState;
            GameDataStore = gameDataStore;
            Presenter = presenter;
            Settings = settings;
            ResumeProgress = resumeProgress;
        }

        public void ConsumeResumeProgress() => ResumeProgress = null;

        public void SetFirstDeploymentRollOrder(List<ITeam> firstDeploymentRollWinner)
        {
            if (FirstDeploymentRollOrder != null)
            {
                throw new InvalidOperationException($"Tried to set roll winner order, but it was already set.");
            }

            FirstDeploymentRollOrder = firstDeploymentRollWinner;
        }

        public void NotifyGameCompleted(GameResult result)
        {
            OnGameCompleted?.Invoke(result);
        }
    }
}