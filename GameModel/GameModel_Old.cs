
using FDG.Data;
using FDG.Stages;
using FDG.StateMachine;
using FutureOfDarkGrimness.StateMachine.StateMachineBuilders;

namespace FDG
{
    /// <summary>
    /// TODO: Made this before networking, now I want to do things differently, but not ready to delete all this yet.
    /// </summary>
    public class GameModel_Old
    {
        private IGameContext GameContext => _gameContext;

        public IReadableGameDataStore GameDataStore => _gameDataStore;

        public ICommandProcessor CommandProcessor => _commandProcessor;

        public ITableState TableState => _tableState;

        public IStateMachine StateMachine => _stateMachine;


        private GameContext _gameContext;

        private GameDataStore _gameDataStore;

        private TableState _tableState;

        private CommandProcessor _commandProcessor;

        private StageHandlerRegistry _stageHandlerRegistry;

        private TextOutputRelayer _textOutputRelayer;

        private StateMachine<IGameContext> _stateMachine;


        public GameModel_Old(GameSettings gameSettings, StageHandlerRegistry stageHandlerRegistry)
        {
            _stageHandlerRegistry = stageHandlerRegistry;
            _textOutputRelayer = new TextOutputRelayer();

            IDiceRoller diceRoller = gameSettings.RandomnessType switch
            {
                ERandomnessType.Probabilistic => new ProbabilisticDiceRoller(),
                ERandomnessType.Realistic => new RealisticDiceRoller(),
                _ => throw new ArgumentOutOfRangeException()
            };

            _gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(64)
                .RegisterType<float>(64)
                .RegisterType<Position>(64)
                .RegisterType<ModelData>(64)
                .RegisterType<TeamData>(2)
                .RegisterType<PlayerData>(2)
                .RegisterType<UnitData>(32)
                .RegisterType<ArmyData>(8)
                .RegisterType<Terrain>(8)
                .Build();

            _commandProcessor = new CommandProcessor(_gameDataStore);

            _tableState = new TableState(_gameDataStore);

            _gameContext = new GameContext(_textOutputRelayer, diceRoller, stageHandlerRegistry, _tableState,
                _gameDataStore, _commandProcessor);

            //TODO: Get this procedurally depending on settings.
            GDFStateMachineBuilder gDFStateMachineBuilder = new GDFStateMachineBuilder();
            _stateMachine = new StateMachine<IGameContext>(gDFStateMachineBuilder, _gameContext);
        }

        public void Begin()
        {
            _stateMachine.Enter(_gameContext);
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
