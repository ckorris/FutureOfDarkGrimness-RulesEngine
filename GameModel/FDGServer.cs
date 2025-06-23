using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Synchronization;
using FDG.Players;
using FDG.Stages;
using FDG.TextInterface;
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

            AddTeamDataToGameDataStore(playerSlots, gameDataStore);

            LogAndChatMessageRelayer chatMessageRelayer = new LogAndChatMessageRelayer(_playerSlotManager);

            ITextOutput textOutput = new PlayerLogSender(chatMessageRelayer);

            TableState tableState = new TableState(_gameDataStore);

            _gameContext = new GameContext(textOutput, GetDiceRoller(gameSettings), _playerSlotManager, 
                tableState, 
                _gameDataStore);

            _stateMachine = new StateMachine<IGameContext>(new GDFStateMachineBuilder(), _gameContext);

            //For test, make a thing. 
            //LoadTestData();

            _ = LaunchStateMachineOnceReady(_stateMachine, _gameContext);
        }

        private void AddTeamDataToGameDataStore(PlayerSlot[] playerSlots, IReadWriteableGameDataStore gameDataStore)
        {
            Dictionary<int, List<PlayerID>> teams = new Dictionary<int, List<PlayerID>>();

            //This assumes team numbers are unique already. But if we ever use -1 for
            //those not on a team or something like that, there will be issues.

            for(int i = 0; i < playerSlots.Length; i++)
            {
                PlayerSlot slot = playerSlots[i];

                int teamSlot = slot.TeamNumber;
                if(teams.ContainsKey(teamSlot) == false)
                {
                    teams.Add(teamSlot, new List<PlayerID>());
                }

                teams[teamSlot].Add(slot.PlayerID);
            }
            
            foreach(KeyValuePair<int, List<PlayerID>> kvp in teams)
            {
                TeamData teamData = new TeamData(kvp.Key, kvp.Value);
                DataReference teamReference = gameDataStore.Create(teamData);
            }

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
            System.Diagnostics.Debug.WriteLine("Awaiting players to be ready.");
            await _playerSlotManager.WaitUntilAllSlotsReady(); //Half a second. At least lets us test before implementing this.
            System.Diagnostics.Debug.WriteLine("All players are ready.");

            _ = stateMachine.Enter(context);
        }

        /*
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
        */
    }
}
