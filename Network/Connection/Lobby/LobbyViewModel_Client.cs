using F.GameModel;
using FDG;
using FDG.Data;
using FDG.EngineInterface;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.SaveLoad;
using FutureOfDarkGrimness.Network.Messages;
using System.Diagnostics;
using System.Reactive.Subjects;

namespace FutureOfDarkGrimness.Network.Connection.Lobby
{
    public class LobbyViewModel_Client : ILobbyViewModel
    {
        public bool HasHostPrivileges => false;

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

        private BehaviorSubject<int> _settings_ArmyPoints;
        private BehaviorSubject<int> _settings_TerrainPieceCount;
        private BehaviorSubject<ERandomnessType> _settings_RandomnessType;
        private BehaviorSubject<ETurnStyle> _settings_TurnMethod;

        private PlayerID? _thisPlayerID = null;
        private string _thisPlayerName;

        IReadWriteableGameDataStore _gameDataStore;

        private IMessageBusClient _messageBusClient;


        private const string SERVER_JOIN_MESSAGE = "Welcome to the server.";

        public event Action<IFDGGame>? OnLaunched;

        public LobbyViewModel_Client(string thisPlayerName, INetworkClient networkclient)
        {
             _gameDataStore = GameDataStore.GameDataStoreBuilder.GetDefault();

            _messageBusClient = new MessageBusClient_Networked(networkclient, _gameDataStore);

            _thisPlayerName = thisPlayerName;

            _serverName = new BehaviorSubject<string>("");
            _chatMessagesSubject = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(0);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(0);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(ERandomnessType.Realistic);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(ETurnStyle.Standard);

            //Init empty player list. The host should update us.
            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfoSummary>>(new List<LobbyPlayerInfoSummary>());

            _messageBusClient.RegisterForMessageEvent<LobbyPlayerIDAssignment>(OnPlayerIDAssignmentReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyServerNameMessage>(OnServerNameUpdateReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyPlayerListUpdate>(OnPlayerListUpdateReceived);
            _messageBusClient.RegisterForMessageEvent<LaunchGameMessage>(OnLaunchGameMessageReceived);
            _messageBusClient.RegisterForMessageEvent<LobbyGameSettingsUpdate>(OnGameSettingsUpdateReceived);

            //Send greeting. 
            NewLobbyClientGreeting greeting = new NewLobbyClientGreeting(thisPlayerName);
            _messageBusClient.SendCommandToHostAsync(greeting);

            //Show init message in chatbox.
            AddMessageToLocalList(new LobbyChatMessage("System", SERVER_JOIN_MESSAGE));
        }

        public void AddLocalPlayer()
        {
            throw new InvalidOperationException("Tried to add local player when not the host.");
        }

        public void SendMessage(string message)
        {
            Debug.WriteLine($"Sending message: {message}");

            LobbyChatMessage_FromClient chatMessage = new LobbyChatMessage_FromClient(_thisPlayerName, message);

            _messageBusClient.SendCommandToHostAsync(chatMessage);
        }

        private void AddMessageToLocalList(LobbyChatMessage chatMessage)
        {
            _chatMessagesSubject.OnNext(chatMessage);
            _chatMessages.Add(chatMessage);
        }

        public bool TryLaunchGame(out string? failReason)
        {
            failReason = "Can't launch the game as the client.";
            return false;
        }

        public void Dispose()
        {
            _messageBusClient.DeregisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
        }

        private void OnPlayerIDAssignmentReceived(LobbyPlayerIDAssignment assignment)
        {
            _thisPlayerID = assignment.playerID;
        }

        private void OnChatMessageReceived(LobbyChatMessage message)
        {
            Debug.WriteLine($"Received chat message as client: {message.Message}");

            AddMessageToLocalList(message);
        }

        private void OnServerNameUpdateReceived(LobbyServerNameMessage lobbyServerNameMessage)
        {
            _serverName.OnNext(lobbyServerNameMessage.ServerName);
        }

        private void OnPlayerListUpdateReceived(LobbyPlayerListUpdate playerListUpdate)
        {
            _playerInfos.OnNext(playerListUpdate.PlayerInfoList);
        }

        private void OnGameSettingsUpdateReceived(LobbyGameSettingsUpdate gameSettingsUpdate)
        {
            if (_settings_ArmyPoints.Value != gameSettingsUpdate.GameSettings.ArmyPoints)
            {
                _settings_ArmyPoints.OnNext(gameSettingsUpdate.GameSettings.ArmyPoints);
            }
            if (_settings_TerrainPieceCount.Value != gameSettingsUpdate.GameSettings.TerrainPieceCount)
            {
                _settings_TerrainPieceCount.OnNext(gameSettingsUpdate.GameSettings.TerrainPieceCount);
            }
            if (_settings_RandomnessType.Value != gameSettingsUpdate.GameSettings.RandomnessType)
            {
                _settings_RandomnessType.OnNext(gameSettingsUpdate.GameSettings.RandomnessType);
            }
            if (_settings_TurnMethod.Value != gameSettingsUpdate.GameSettings.TurnStyle)
            {
                _settings_TurnMethod.OnNext(gameSettingsUpdate.GameSettings.TurnStyle);
            }
        }

        private void OnLaunchGameMessageReceived(LaunchGameMessage launchGameMessage)
        {
            Debug.WriteLine($"Received launch game message. ");

            if(_thisPlayerID.HasValue == false)
            {
                throw new InvalidOperationException("Tried to launch game without a PlayerID being assigned.");
            }

            FDGGame_AsClient fdgGame = new FDGGame_AsClient(_gameDataStore, _messageBusClient, _thisPlayerID.Value);

            OnLaunched?.Invoke(fdgGame);
        }

        public void SetArmyPoints(int armyPoints)
        {
            throw new InvalidOperationException("Tried to set army points when not the host.");
        }

        public void SetTerrainCount(int terrainCount)
        {
            throw new InvalidOperationException("Tried to set terrain count when not the host.");

        }

        public void SetRandomnessType(ERandomnessType randomnessType)
        {
            throw new InvalidOperationException("Tried to set randomness type when not the host.");

        }

        public void SetTurnStyle(ETurnStyle turnStyle)
        {
            throw new InvalidOperationException("Tried to set turn style when not the host.");

        }

        public bool CheckCanModifyPlayerIDInfo(PlayerID playerID)
        {
            //If we haven't been assigned a value yet, assume no.
            return _thisPlayerID.HasValue && _thisPlayerID.Value == playerID;
        }

        public void UpdateArmyListFile(PlayerID playerId, ArmyListFile armyListFile)
        {
            if(_thisPlayerID.HasValue == false)
            {
                throw new Exception($"Tried to update army list before we've been assigned an ID.");
            }

            if(playerId !=  _thisPlayerID.Value)
            {
                throw new InvalidOperationException("Client to update an army list for the wrong player. " +
                    $"Client's ID: {_thisPlayerID.Value} Update attempt ID: {playerId}");
            }

            ArmyListUpdateMessage message = new ArmyListUpdateMessage(playerId, armyListFile);

            _messageBusClient.SendCommandToHostAsync(message);
        }
    }
}
