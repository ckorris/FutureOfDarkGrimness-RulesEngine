using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.Samples;
using FDG.StageResolution;
using FDG.Stages;
using FDG.StateMachine;
using FutureOfDarkGrimness.StateMachine.StateMachineBuilders;

namespace FDG.GameModel
{
    public class FDGServer
    {
        private IReadWriteableGameDataStore _gameDataStore;
        private FDGHost _host;
        private GameDataUpdateSender _synchronizer;
        private PlayerSlotManager _playerSlotManager;
        private GameContext _gameContext;
        private StateMachine<IGameContext> _stateMachine;

        public FDGServer(IReadWriteableGameDataStore gameDataStore, FDGHost fdgHost, GameSettings gameSettings,
            PlayerSlot[] playerSlots)
        {
            _gameDataStore = gameDataStore;
            _host = fdgHost;
            _synchronizer = new GameDataUpdateSender(gameDataStore, fdgHost);

            //For players/player slots, work backwards from here to create what you need to send updates to players,
            //then what you need to make that thing, then what you need for that, etc. until you lead back to these args.

            _playerSlotManager = new PlayerSlotManager(playerSlots);

            TableState tableState = new TableState(_gameDataStore);

            //TODO: Below has stage handlers assigned, but I'm removing this. That'll break hard. We can't run the game until that's
            //removed from all stages.
            _gameContext = new GameContext(GetTextOutput(), GetDiceRoller(gameSettings), _playerSlotManager, 
                handlers: null, 
                tableState, 
                _gameDataStore);

            _stateMachine = new StateMachine<IGameContext>(new GDFStateMachineBuilder(), _gameContext);

            //For test, make a thing. 
            LoadTestData();

            //TEMP TEST
            return;

            _ = LaunchStateMachineOnceReady(_stateMachine, _gameContext);
        }

        private ITextOutput GetTextOutput()
        {
            return new BasicConsoleLogger();
        }

        private IDiceRoller GetDiceRoller(GameSettings gameSettings)
        {
            IDiceRoller diceRoller = gameSettings.RandomnessType switch
            {
                ERandomnessType.Probabilistic => new ProbabilisticDiceRoller(),
                ERandomnessType.Realistic => new RealisticDiceRoller(),
                _ => throw new ArgumentOutOfRangeException()
            };

            return diceRoller;
        }

        private async Task LaunchStateMachineOnceReady(StateMachine<IGameContext> stateMachine, IGameContext context)
        {
            //TODO: Wait for all clients to indicate that they are connected and ready.
            //Await something.
            await Task.Delay(500); //Half a second. At least lets us test before implementing this.

            _ = stateMachine.Enter(context);
        }

        private void LoadTestData()
        {
            float baseRadiusInches = 0.75f;
            List<Weapon> weapons = new List<Weapon>() { new Weapon("Weapon 1", 6, 2, 1, new HashSet<ISpecialRule_Weapon>()) };
            List<SpecialRule> specialRules = new List<SpecialRule>() { new Rending() };

            int perTeamModelCount = 5;
            float spacing = 2.5f;

            float startX = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f - (perTeamModelCount / 2f * spacing);
            float team1StartY = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            float team2StartY = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;

            //Team 1.
            for (int i = 0; i < perTeamModelCount; i++)
            {
                Position position = new Position(startX + i * spacing, team1StartY);

                ModelData modelData = new ModelData(baseRadiusInches, weapons, specialRules, position, _gameDataStore);
                _gameDataStore.Create(modelData);
            }

            //Team 2.
            for (int i = 0; i < perTeamModelCount; i++)
            {
                Position position = new Position(startX + i * spacing, team2StartY);

                ModelData modelData = new ModelData(baseRadiusInches, weapons, specialRules, position, _gameDataStore);
                _gameDataStore.Create(modelData);
            }
        }
    }
}
