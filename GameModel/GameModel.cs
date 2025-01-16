
using FDG.Data;
using FDG.Stages;
using FDG.StateMachine;
using FutureOfDarkGrimness.StateMachine.StateMachineBuilders;

namespace FDG
{
    public class GameModel
    {
        private IGameContext GameContext => _gameContext;

        public IReadableGameDataStore GameDataStore => _gameDataStore;

        public ICommandProcessor CommandProcessor => _commandProcessor;

        public ITableState TableState => _tableState;

        public IStateMachine StateMachine { get; private set; }


        private GameContext _gameContext;

        private GameDataStore _gameDataStore;

        private TableState _tableState;

        private CommandProcessor _commandProcessor;

        private StageHandlerRegistry _stageHandlerRegistry;

        private TextOutputRelayer _textOutputRelayer;

<<<<<<< HEAD
=======
        private GameContext _gameContext;

        private TableState _tableState;

        private GameDataStore _gameDataStore;

        private CommandProcessor _commandProcessor;

        public IStateMachine StateMachine => _stateMachine;

        private StateMachine<IGameContext> _stateMachine;
>>>>>>> c8493aabe47e70fb1389b175fe258235bf29aeef

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

<<<<<<< HEAD
            _gameContext = new GameContext(_textOutputRelayer, diceRoller, stageHandlerRegistry, _tableState);

=======
>>>>>>> c8493aabe47e70fb1389b175fe258235bf29aeef
            _gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<int>(64)
                .RegisterType<float>(64)
                .RegisterType<Position>(64)
                .RegisterType<ModelData>(64)
                .RegisterType<PlayerInfo>(2)
                .RegisterType<UnitData>(32)
                .RegisterType<ArmyData>(8)
                .Build();

            _commandProcessor = new CommandProcessor(_gameDataStore);

            _tableState = new TableState(_gameDataStore);

            _gameContext = new GameContext(_textOutputRelayer, diceRoller, stageHandlerRegistry, _tableState,
                _gameDataStore, _commandProcessor);

            //TODO: Get this procedurally depending on settings.
            GDFStateMachineBuilder gDFStateMachineBuilder = new GDFStateMachineBuilder();
            StateMachine = new StateMachine<IGameContext>(gDFStateMachineBuilder, _gameContext);
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
