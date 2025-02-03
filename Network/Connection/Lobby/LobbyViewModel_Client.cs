using FDG;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FutureOfDarkGrimness.Network.Messages;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace FutureOfDarkGrimness.Network.Connection.Lobby
{
    public class LobbyViewModel_Client : ILobbyViewModel
    {
        public bool HasHostPrivileges => false;

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

        private string _thisPlayerName;

        private ICommandDispatcher _commandDispatcher;

        private FDGClient _client;

        private const string SERVER_JOIN_MESSAGE = "Welcome to the server.";

        public event Action? OnLaunched;

        public LobbyViewModel_Client(string thisPlayerName, FDGClient client)
        {
            _client = client;

            _thisPlayerName = thisPlayerName;

            _serverName = new BehaviorSubject<string>("");
            _chatMessages = new ReplaySubject<LobbyChatMessage>();

            _settings_ArmyPoints = new BehaviorSubject<int>(0);
            _settings_TerrainPieceCount = new BehaviorSubject<int>(0);
            _settings_RandomnessType = new BehaviorSubject<ERandomnessType>(ERandomnessType.Realistic);
            _settings_TurnMethod = new BehaviorSubject<ETurnStyle>(ETurnStyle.Standard);

            //Init empty player list. The host should update us.
            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfo>>(new List<LobbyPlayerInfo>());

            _commandDispatcher = client;

            _commandDispatcher.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _commandDispatcher.RegisterForMessageEvent<LobbyServerNameMessage>(OnServerNameUpdateReceived);
            _commandDispatcher.RegisterForMessageEvent<LobbyPlayerListUpdate>(OnPlayerListUpdateReceived);
            _commandDispatcher.RegisterForMessageEvent<LaunchGameMessage>(OnLaunchGameMessageReceived);
            _commandDispatcher.RegisterForMessageEvent<LobbyGameSettingsUpdate>(OnGameSettingsUpdateReceived);

            //Send greeting. 
            NewLobbyClientGreeting greeting = new NewLobbyClientGreeting(thisPlayerName);
            _commandDispatcher.SendCommandAsync(greeting);

            //Show init message in chatbox.
            _chatMessages.OnNext(new LobbyChatMessage("System", SERVER_JOIN_MESSAGE));
        }

        public void SendMessage(string message)
        {
            Debug.WriteLine($"Sending message: {message}");

            LobbyChatMessage chatMessage = new LobbyChatMessage(_thisPlayerName, message);

            _commandDispatcher.SendCommandAsync(chatMessage);
        }


        public bool TryLaunchGame(out string? failReason)
        {
            failReason = "Can't launch the game as the client.";
            return false;
        }

        public void Dispose()
        {
            _commandDispatcher.DeregisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
        }

        private void OnChatMessageReceived(LobbyChatMessage message)
        {
            Debug.WriteLine($"Received chat message as client: {message.Message}");

            _chatMessages.OnNext(message);
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

            OnLaunched?.Invoke();
        }

        public void SetArmyPoints(int armyPoints)
        {
            throw new NotImplementedException();
        }

        public void SetTerrainCount(int terrainCount)
        {
            throw new NotImplementedException();
        }

        public void SetRandomnessType(ERandomnessType randomnessType)
        {
            throw new NotImplementedException();
        }

        public void SetTurnStyle(ETurnStyle turnStyle)
        {
            throw new NotImplementedException();
        }
    }
}
