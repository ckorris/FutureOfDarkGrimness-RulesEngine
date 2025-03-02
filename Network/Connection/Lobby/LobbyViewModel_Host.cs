using FDG.Data;
using FDG.EngineInterface;
using FDG.GameModel;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.StageResolution;
using FutureOfDarkGrimness.Network.Messages;
using System.Diagnostics;
using System.Reactive.Subjects;

namespace FDG.Network.Connection
{
    public class LobbyViewModel_Host : ILobbyViewModel
    {
        public bool HasHostPrivileges => true;

        public IObservable<string> ServerName => _serverName;

        public IObservable<LobbyChatMessage> ChatMessages => _chatMessages;

        public IObservable<IReadOnlyList<LobbyPlayerInfo>> PlayerInfos => _playerInfos;

        public IObservable<int> Settings_ArmyPoints => _settings_ArmyPoints;
        public IObservable<int> Settings_TerrainPieceCount => _settings_TerrainPieceCount;
        public IObservable<ERandomnessType> Settings_RandomnessType => _settings_RandomnessType;
        public IObservable<ETurnStyle> Settings_TurnStyle => _settings_TurnMethod;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessages;

        private BehaviorSubject<IReadOnlyList<LobbyPlayerInfo>> _playerInfos;

        private BehaviorSubject<int> _settings_ArmyPoints;
        private BehaviorSubject<int> _settings_TerrainPieceCount;
        private BehaviorSubject<ERandomnessType> _settings_RandomnessType;
        private BehaviorSubject<ETurnStyle> _settings_TurnMethod;


        private FDGHost _host;

        private string _hostPlayerName;

        private ICommandDispatcher _commandDispatcher;

        private const string SERVER_START_MESSAGE = "Server started successfully.";
        private const string LAUNCHING_GAME_MESSAGE = "About to launch game.";

        public event Action<IFDGGame, AssignStageResolverRegistryDelegate>? OnLaunched;

        private GameSettings _gameSettings = GameSettings.GetDefault();

        public LobbyViewModel_Host(string hostPlayerName, string serverName, string? password, FDGHost host)
        {
            _host = host;
            _hostPlayerName = hostPlayerName;

            _serverName = new BehaviorSubject<string>(serverName);
            _chatMessages = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(_gameSettings.ArmyPoints);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(_gameSettings.TerrainPieceCount);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(_gameSettings.RandomnessType);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(_gameSettings.TurnStyle);

            //First init just ourselves.
            List<LobbyPlayerInfo> initialLobbyPlayerInfos = new List<LobbyPlayerInfo>()
            {
                new LobbyPlayerInfo(hostPlayerName, 0, EPlayerType.Local, new ConnectionID(Guid.Empty))
            };

            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfo>>(initialLobbyPlayerInfos);

            _commandDispatcher = host;

            host.OnNewClientConnected += OnNewClientConnected;
            host.OnClientDisconnected += OnClientDisconnected;

            _commandDispatcher.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _commandDispatcher.RegisterForMessageEvent<NewLobbyClientGreeting>(OnReceiveNewClientGreeting);

            //Show init message in chatbox.
            _chatMessages.OnNext(new LobbyChatMessage("System", SERVER_START_MESSAGE));
        }


        public void SendMessage(string message)
        {
            LobbyChatMessage chatMessage = new LobbyChatMessage(_hostPlayerName, message);
            _commandDispatcher.SendCommandAsync(chatMessage);

            _chatMessages.OnNext(chatMessage);
        }

        private void OnNewClientConnected(ConnectionID connectionID)
        {
            Debug.WriteLine($"{nameof(LobbyViewModel_Host)}.{nameof(OnNewClientConnected)}.");
            //Test: Just send it the current player list.

            //Maybe do nothing?
        }

        private void OnReceiveNewClientGreeting(NewLobbyClientGreeting greeting, ConnectionID connectionID)
        {
            Debug.WriteLine($"Received greeting from new client: {greeting.PlayerName}");

            //Send the server name.
            LobbyServerNameMessage lobbyServerNameMessage = new LobbyServerNameMessage(_serverName.Value);
            _commandDispatcher.SendCommandAsync(lobbyServerNameMessage);

            //TODO: Have something behind the player info list instead of doing this.
            int tempTeamNumber = _playerInfos.Value.Count + 1;

            List<LobbyPlayerInfo> playerInfos = new List<LobbyPlayerInfo>(_playerInfos.Value)
            {
                new LobbyPlayerInfo(greeting.PlayerName, tempTeamNumber, EPlayerType.Network, connectionID)
            };

            LobbyPlayerListUpdate playerListUpdateMessage = new LobbyPlayerListUpdate(playerInfos);
            _commandDispatcher.SendCommandAsync(playerListUpdateMessage);

            LobbyGameSettingsUpdate gameSettingsUpdate = new LobbyGameSettingsUpdate(_gameSettings);
            _commandDispatcher.SendCommandAsync(gameSettingsUpdate);

            _playerInfos.OnNext(playerInfos);
        }

        private void OnClientDisconnected(ConnectionID disconnectedClientID)
        {
            //TODO.
        }

        private void OnChatMessageReceived(LobbyChatMessage chatMessage, ConnectionID _)
        {
            Debug.WriteLine($"Received chat message as host: {chatMessage.Message}");

            //Relay it to everyone else.
            _commandDispatcher.SendCommandAsync(chatMessage); //TODO: Release the byte array but gotta be careful on timing.

            //Put the chat message in our own box.
            _chatMessages.OnNext(chatMessage);
        }

        public void Dispose()
        {
            _commandDispatcher.DeregisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _host.OnNewClientConnected -= OnNewClientConnected;
            _host.OnClientDisconnected -= OnClientDisconnected;
        }

        public bool TryLaunchGame(out string? failReason)
        {
            Debug.WriteLine("TryLaunchGame.");

            //If we ever require readying up, this is where that can go.
            _ = Launch();
            failReason = null;
            return true;
        }

        private async Task Launch()
        {
            LobbyChatMessage gameStartingMessage = new LobbyChatMessage("System", LAUNCHING_GAME_MESSAGE);
            _chatMessages.OnNext(gameStartingMessage);
            await _commandDispatcher.SendCommandAsync(gameStartingMessage);
            await Task.Delay(300);

            //Give a quick tribute.
            LobbyChatMessage tributeMessage = new LobbyChatMessage("Mukumioke", "buck futter");
            _chatMessages.OnNext(tributeMessage);
            await _commandDispatcher.SendCommandAsync(tributeMessage);
            await Task.Delay(50);

            //Maybe something else should make these but eh.
            GameDataStore gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            //Make a player controller for each player. TODO: This is overly simplistic for now.
            PlayerSlot[] playerSlots = GetPlayerSlots(gameDataStore, 
                out AssignStageResolverRegistryDelegate assignStageResolverRegistryDelegate);


            FDGServer server = new FDGServer(gameDataStore, _host, _gameSettings, playerSlots);
            FDGGame_AsLocal gameModel = new FDGGame_AsLocal(gameDataStore);

            OnLaunched?.Invoke(gameModel, assignStageResolverRegistryDelegate);

            LaunchGameMessage launchGameMessage = new LaunchGameMessage();
            await _commandDispatcher.SendCommandAsync(launchGameMessage);
        }
        
        private PlayerSlot[] GetPlayerSlots(IReadableGameDataStore gameDataStore, 
            out AssignStageResolverRegistryDelegate assignStageHandlerRegistryDelegate)
        {
            PlayerSlot[] playerSlots = new PlayerSlot[_playerInfos.Value.Count];

            //Local players will need to have registries assigned locally, so cache them specifically for this.
            List<LocalPlayerController> localPlayerControllers = new List<LocalPlayerController>();

            for (int i = 0; i < playerSlots.Length; i++)
            {
                LobbyPlayerInfo lobbyPlayerInfo = _playerInfos.Value[i];

                PlayerSlot playerSlot = new PlayerSlot(i, lobbyPlayerInfo.TeamNumber);

                switch(lobbyPlayerInfo.PlayerType)
                {
                    case EPlayerType.Local:
                        LocalPlayerController localPlayerController = new LocalPlayerController(lobbyPlayerInfo.PlayerName, playerSlot.PlayerID);
                        localPlayerControllers.Add(localPlayerController);
                        playerSlots[i].AssignPlayerController(localPlayerController);
                        break;
                    case EPlayerType.Network:
                        NetworkPlayerController networkPlayerController = new NetworkPlayerController(lobbyPlayerInfo.PlayerName, playerSlot.PlayerID,
                            lobbyPlayerInfo.connectionID, _commandDispatcher, gameDataStore);
                            break;
                    case EPlayerType.AI:
                        throw new NotImplementedException();
                    default:
                        throw new ArgumentOutOfRangeException();

                }


                playerSlots[i] = playerSlot;
            }

            void AssignStageHandlerRegistry(StageResolverRegistry stageResolverRegistry)
            {
                foreach(LocalPlayerController localPlayerController in localPlayerControllers)
                {
                    localPlayerController.AssignStageResolverRegistry(stageResolverRegistry);
                }
            }

            assignStageHandlerRegistryDelegate = AssignStageHandlerRegistry;

            return playerSlots;
        }

        public void SetArmyPoints(int armyPoints)
        {
            if (armyPoints > 0)
            {
                _settings_ArmyPoints.OnNext(armyPoints);
                _gameSettings.ArmyPoints = armyPoints;
                _commandDispatcher.SendCommandAsync(new LobbyGameSettingsUpdate(_gameSettings));

            }
            else
            {
                _settings_ArmyPoints.OnNext(_settings_ArmyPoints.Value);
            }
        }

        public void SetTerrainCount(int terrainCount)
        {
            if (terrainCount > 0)
            {
                _settings_TerrainPieceCount.OnNext(terrainCount);
                _gameSettings.TerrainPieceCount = terrainCount;
                _commandDispatcher.SendCommandAsync(new LobbyGameSettingsUpdate(_gameSettings));
            }
            else
            {
                _settings_TerrainPieceCount.OnNext(_settings_TerrainPieceCount.Value);
            }
        }

        public void SetRandomnessType(ERandomnessType randomnessType)
        {
            if (Enum.IsDefined(randomnessType))
            {
                _settings_RandomnessType.OnNext(randomnessType);
                _gameSettings.RandomnessType = randomnessType;
                _commandDispatcher.SendCommandAsync(new LobbyGameSettingsUpdate(_gameSettings));

            }
            else
            {
                _settings_RandomnessType.OnNext(_settings_RandomnessType.Value);
            }
        }

        public void SetTurnStyle(ETurnStyle turnStyle)
        {
            if (Enum.IsDefined(turnStyle))
            {
                _settings_TurnMethod.OnNext(turnStyle);
                _gameSettings.TurnStyle = turnStyle;
                _commandDispatcher.SendCommandAsync(new LobbyGameSettingsUpdate(_gameSettings));

            }
            else
            {
                _settings_TurnMethod.OnNext(_settings_TurnMethod.Value);
            }
        }
    }
}
