
using FDG.Data;
using FDG.Stages;
using FDG.StateMachine;
using FutureOfDarkGrimness.StateMachine.StateMachineBuilders;

namespace FDG
{
    public class GameModel
    {
        private StageHandlerRegistry _stageHandlerRegistry;

        private TextOutputRelayer _textOutputRelayer;

        private GameContext _gameContext;

        private TableState _tableState;

        private GameDataStore _gameDataStore;

        private CommandProcessor _commandProcessor;

        private IStateMachine _stateMachine;

        public GameModel(GameSettings gameSettings, StageHandlerRegistry stageHandlerRegistry)
        {
            _stageHandlerRegistry = stageHandlerRegistry;
            _textOutputRelayer = new TextOutputRelayer();

            IDiceRoller diceRoller = gameSettings.RandomnessType switch
            {
                ERandomnessType.Probabilistic => new ProbabilisticDiceRoller(),
                ERandomnessType.Realistic => new RealisticDiceRoller(),
                _ => throw new ArgumentOutOfRangeException()
            };

            //TODO: I think I wanna refactor these to be more data-oriented, but not sure how to change this yet.
            PlayerState playerState = new PlayerState();
            ArmyState armyState = new ArmyState();
            _tableState = new TableState(playerState, armyState);

            _gameContext = new GameContext(_textOutputRelayer, diceRoller, stageHandlerRegistry, _tableState);

            _gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(64)
                .RegisterType<float>(64)
                .RegisterType<Position>(64)
                .RegisterType<Model>(64)
                .RegisterType<Unit>(32)
                .RegisterType<Army>(8)
                .Build();

            _commandProcessor = new CommandProcessor(_gameDataStore);

            //TODO: Get this procedurally depending on settings.
            GDFStateMachineBuilder gDFStateMachineBuilder = new GDFStateMachineBuilder();
            _stateMachine = new StateMachine<IGameContext>(gDFStateMachineBuilder, _gameContext);
        }


        public void RegisterTextOutput(ITextOutput textOutput)
        {
            _textOutputRelayer.RegisterTextOutput(textOutput);
        }

        private class TextOutputRelayer : ITextOutput
        {
            private List<ITextOutput> _outputs = new List<ITextOutput>();

            public void RegisterTextOutput(ITextOutput textOutput)
            {
                _outputs.Add(textOutput);
            }

            public void Log(string message)
            {
                foreach(ITextOutput output in _outputs)
                {
                    output.Log(message);
                }
            }
        }

    }
}
