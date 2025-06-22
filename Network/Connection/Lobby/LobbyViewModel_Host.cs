using FDG.Data;
using FDG.EngineInterface;
using FDG.GameModel;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FutureOfDarkGrimness.Network.Messages;
using FutureOfDarkGrimness.Players;
using NUnit.Framework.Constraints;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace FDG.Network.Connection
{
    public class LobbyViewModel_Host : ILobbyViewModel
    {
        public bool HasHostPrivileges => true;

        public IObservable<string> ServerNameObservable => _serverName;

        public IObservable<LobbyChatMessage> ChatMessagesObservable => _chatMessagesSubject;

        public IObservable<IReadOnlyList<LobbyPlayerInfoSummary>> PlayerInfosObservable => _playerInfos;

        public IObservable<int> ArmyPointsObservable => _settings_ArmyPoints;
        public IObservable<int> TerrainPieceCountObservable => _settings_TerrainPieceCount;
        public IObservable<ERandomnessType> RandomnessTypeObservable => _settings_RandomnessType;
        public IObservable<ETurnStyle> TurnStyleObservable => _settings_TurnMethod;

        public string ServerName => _serverName.Value;

        public IReadOnlyList<LobbyChatMessage> ChatMessages => _chatMessages;

        public IReadOnlyList<LobbyPlayerInfoSummary> PlayerInfos => _playerInfos.Value;

        public int ArmyPoints => _settings_ArmyPoints.Value;

        public int TerrainCount => _settings_TerrainPieceCount.Value;

        public ERandomnessType RandomnessType => _settings_RandomnessType.Value;

        public ETurnStyle TurnStyle => _settings_TurnMethod.Value;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessagesSubject;
        private readonly List<LobbyChatMessage> _chatMessages = new List<LobbyChatMessage>();

        private BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>> _playerInfos;
        private Dictionary<PlayerID, LobbyPlayerInfoFull> _playerInfosFull = new Dictionary<PlayerID, LobbyPlayerInfoFull>();

        private BehaviorSubject<int> _settings_ArmyPoints;
        private BehaviorSubject<int> _settings_TerrainPieceCount;
        private BehaviorSubject<ERandomnessType> _settings_RandomnessType;
        private BehaviorSubject<ETurnStyle> _settings_TurnMethod;


        private FDGHost _host;

        private string _hostPlayerName;

        private ICommandDispatcher _commandDispatcher;

        private const string SERVER_START_MESSAGE = "Server started successfully.";
        private const string LAUNCHING_GAME_MESSAGE = "About to launch game.";

        public event Action<IFDGGame>? OnLaunched;

        private GameSettings _gameSettings = GameSettings.GetDefault();

        

        public LobbyViewModel_Host(string hostPlayerName, string serverName, string? password, FDGHost host)
        {
            _host = host;
            _hostPlayerName = hostPlayerName;
            PlayerID thisPlayerID = new PlayerID(Guid.NewGuid());

            _serverName = new BehaviorSubject<string>(serverName);
            _chatMessagesSubject = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(_gameSettings.ArmyPoints);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(_gameSettings.TerrainPieceCount);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(_gameSettings.RandomnessType);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(_gameSettings.TurnStyle);

            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>>(new List<LobbyPlayerInfoSummary>());

            _commandDispatcher = host;

            host.OnNewClientConnected += OnNewClientConnected;
            host.OnClientDisconnected += OnClientDisconnected;

            _commandDispatcher.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _commandDispatcher.RegisterForMessageEvent<NewLobbyClientGreeting>(OnReceiveNewClientGreeting);
            _commandDispatcher.RegisterForMessageEvent<ArmyListUpdateMessage>(OnArmyListFileUpdateReceived);

            //Show init message in chatbox.
            AddMessageToLocalList(new LobbyChatMessage("System", SERVER_START_MESSAGE));

            //First init just ourselves.
            //ArmyListSummary tempSummary = new ArmyListSummary("Manhandlers", "Battle Brothers", 2000);

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(hostPlayerName, null, ETeamOption.Team1,
                EPlayerType.Local, new ConnectionID(Guid.Empty), thisPlayerID);
            _playerInfosFull.Add(thisPlayerID, newLobbyPlayerInfo);
            UpdateInfoSummariesFromFullList();
        }


        public void SendMessage(string message)
        {
            LobbyChatMessage chatMessage = new LobbyChatMessage(_hostPlayerName, message);
            _commandDispatcher.SendCommandAsync(chatMessage);

            AddMessageToLocalList(chatMessage);
        }

        private void AddMessageToLocalList(LobbyChatMessage chatMessage)
        {
            _chatMessagesSubject.OnNext(chatMessage);
            _chatMessages.Add(chatMessage);
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

            //Assign a player ID to this person. 
            //TODO: I may have decided to give players their IDs elsewhere but I can't remember, double check that.
            PlayerID newClientPlayerID = new PlayerID(Guid.NewGuid());
            _commandDispatcher.SendCommandAsync(new LobbyPlayerIDAssignment(newClientPlayerID));

            //Send the server name.
            LobbyServerNameMessage lobbyServerNameMessage = new LobbyServerNameMessage(_serverName.Value);
            _commandDispatcher.SendCommandAsync(lobbyServerNameMessage);

            //TODO: Have something behind the player info list instead of doing this.
            int tempTeamNumber = _playerInfos.Value.Count + 1;

            //ArmyListSummary tempSummary = new ArmyListSummary("Knifeybois", "Alien Hives", 2000);

            LobbyPlayerInfoFull newLobbyPlayerInfo = new LobbyPlayerInfoFull(greeting.PlayerName, null, (ETeamOption)tempTeamNumber,
                EPlayerType.Network, connectionID, newClientPlayerID);
            _playerInfosFull.Add(newClientPlayerID, newLobbyPlayerInfo);
            UpdateInfoSummariesFromFullList();

            LobbyGameSettingsUpdate gameSettingsUpdate = new LobbyGameSettingsUpdate(_gameSettings);
            _commandDispatcher.SendCommandAsync(gameSettingsUpdate);
        }

        private void OnClientDisconnected(ConnectionID disconnectedConnectionID)
        {
            PlayerID leavingPlayerID = _playerInfosFull.First(info => info.Value.ConnectionID == disconnectedConnectionID).Key;
            _playerInfosFull.Remove(leavingPlayerID);
            UpdateInfoSummariesFromFullList();
        }

        private void UpdateInfoSummariesFromFullList()
        {
            //Not exactly optimized, but done quite infrequently.
            List<LobbyPlayerInfoSummary> infoSummaries = new List<LobbyPlayerInfoSummary>();
            foreach(LobbyPlayerInfoFull fullInfo in _playerInfosFull.Values)
            {
                ArmyListSummary? summary = fullInfo.ArmyListFile != null
                    ? new ArmyListSummary(true, fullInfo.ArmyListFile.Name, fullInfo.ArmyListFile.Faction,
                    fullInfo.ArmyListFile.TotalPoints)
                    : new ArmyListSummary(false, "N/A", "N/A", 0);
                infoSummaries.Add(new LobbyPlayerInfoSummary(fullInfo.PlayerName, summary, fullInfo.TeamNumber, fullInfo.PlayerType,
                    fullInfo.ConnectionID, fullInfo.PlayerID));
            }

            LobbyPlayerListUpdate playerListUpdateMessage = new LobbyPlayerListUpdate(infoSummaries);
            _commandDispatcher.SendCommandAsync(playerListUpdateMessage);

            _playerInfos.OnNext(infoSummaries);
        }

        private void OnChatMessageReceived(LobbyChatMessage chatMessage, ConnectionID _)
        {
            Debug.WriteLine($"Received chat message as host: {chatMessage.Message}");

            //Relay it to everyone else.
            _commandDispatcher.SendCommandAsync(chatMessage); //TODO: Release the byte array but gotta be careful on timing.

            //Put the chat message in our own box.
            AddMessageToLocalList(chatMessage);
        }

        private void OnArmyListFileUpdateReceived(ArmyListUpdateMessage armyUpdate, ConnectionID _)
        {
            UpdateArmyListFile(armyUpdate.playerID, armyUpdate.armyListFile);
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
            AddMessageToLocalList(gameStartingMessage);
            await _commandDispatcher.SendCommandAsync(gameStartingMessage);
            await Task.Delay(300);

            //Give a quick tribute.
            LobbyChatMessage tributeMessage = new LobbyChatMessage("Mukumioke", "buck futter");
            AddMessageToLocalList(tributeMessage);
            await _commandDispatcher.SendCommandAsync(tributeMessage);
            await Task.Delay(50);

            //Maybe something else should make these but eh.
            GameDataStore gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            FDGGame_AsLocal? gameModel = null; //We may not have a local player.

            //Make a player controller for each player.
            PlayerSlot[] playerSlots = new PlayerSlot[_playerInfosFull.Count];

            List<FDGGame_AsLocal> localPlayers = new List<FDGGame_AsLocal>(2);

            LobbyPlayerInfoFull[] lobbyPlayerInfosArray = _playerInfosFull.Values.ToArray();

            for (int i = 0; i < playerSlots.Length; i++)
            {
                LobbyPlayerInfoFull lobbyPlayerInfo = lobbyPlayerInfosArray[i];

                PlayerSlot playerSlot = new PlayerSlot(i, (int)lobbyPlayerInfo.TeamNumber, lobbyPlayerInfo.PlayerID);
                playerSlots[i] = playerSlot;

                switch (lobbyPlayerInfo.PlayerType)
                {
                    case EPlayerType.Local:

                        if(gameModel == null)
                        {
                            gameModel = new FDGGame_AsLocal(gameDataStore);
                        }

                        LocalPlayerController localPlayerController = new LocalPlayerController(lobbyPlayerInfo.PlayerName,
                            playerSlot.PlayerID, gameModel);
                        localPlayers.Add(gameModel);
                        playerSlot.AssignPlayerController(localPlayerController);
                        break;
                    case EPlayerType.Network:
                        NetworkPlayerController networkPlayerController = new NetworkPlayerController(lobbyPlayerInfo.PlayerName, playerSlot.PlayerID,
                            lobbyPlayerInfo.ConnectionID, _commandDispatcher, gameDataStore);
                        playerSlot.AssignPlayerController(networkPlayerController);
                        break;
                    case EPlayerType.AI:
                        throw new NotImplementedException();
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            FDGServer server = new FDGServer(gameDataStore, _host, _gameSettings, playerSlots);

            if (gameModel != null) //Dedicated server really doesn't need to do this.
            {
                OnLaunched?.Invoke(gameModel);
            }

            LaunchGameMessage launchGameMessage = new LaunchGameMessage();
            await _commandDispatcher.SendCommandAsync(launchGameMessage);
        }
        
        /*
        private PlayerSlot[] GetPlayerSlots(IStageResolverRegistry stageResolverRegister, IReadableGameDataStore gameDataStore)
        {
            PlayerSlot[] playerSlots = new PlayerSlot[_playerInfos.Value.Count];

            //Local players will need to have registries assigned locally, so cache them specifically for this.
            List<LocalPlayerController> localPlayerControllers = new List<LocalPlayerController>();

            for (int i = 0; i < playerSlots.Length; i++)
            {
                LobbyPlayerInfoSummary lobbyPlayerInfo = _playerInfos.Value[i];

                PlayerSlot playerSlot = new PlayerSlot(i, (int)lobbyPlayerInfo.TeamNumber);
                playerSlots[i] = playerSlot;

                switch (lobbyPlayerInfo.PlayerType)
                {
                    case EPlayerType.Local:
                        LocalPlayerController localPlayerController = new LocalPlayerController(lobbyPlayerInfo.PlayerName, 
                            playerSlot.PlayerID, stageResolverRegister);
                        localPlayerControllers.Add(localPlayerController);
                        playerSlot.AssignPlayerController(localPlayerController);
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
            }

            return playerSlots;
        }
        */

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

        public bool CheckCanModifyPlayerIDInfo(PlayerID playerID)
        {
            LobbyPlayerInfoSummary? queryPlayer = _playerInfos.Value.FirstOrDefault(info => info.PlayerID == playerID);

            if(queryPlayer == null)
            {
                Debug.WriteLine($"Queried about a player with an ID not found in the list: {playerID.ID}");
                return false;
            }

            return queryPlayer.PlayerType != EPlayerType.Network;
        }

        public void UpdateArmyListFile(PlayerID playerId, ArmyListFile armyListFile)
        {
            if(_playerInfosFull.ContainsKey(playerId) == false)
            {
                Debug.WriteLine($"Couldn't find ID of player {playerId}.");
                return;
            }

            _playerInfosFull[playerId].ArmyListFile = armyListFile;

            UpdateInfoSummariesFromFullList();
        }
    }
}
