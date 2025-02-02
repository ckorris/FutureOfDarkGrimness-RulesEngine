using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FutureOfDarkGrimness.Network.Messages;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace FDG.Network.Connection
{
    public class LobbyViewModel_Host : ILobbyViewModel
    {
        public bool HasHostPrivileges => true;

        public IObservable<string> ServerName => _serverName;

        public IObservable<LobbyChatMessage> ChatMessages => _chatMessages;

        public IObservable<IReadOnlyList<LobbyPlayerInfo>> PlayerInfos => _playerInfos;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessages;

        private BehaviorSubject<IReadOnlyList<LobbyPlayerInfo>> _playerInfos;

        private FDGHost _host;

        private string _hostPlayerName;

        private ICommandDispatcher _commandDispatcher;

        private const string SERVER_START_MESSAGE = "Server started successfully.";

        public event Action? OnLaunched;

        public LobbyViewModel_Host(string hostPlayerName, string serverName, string? password, FDGHost host)
        {
            _host = host;
            _hostPlayerName = hostPlayerName;

            _serverName = new BehaviorSubject<string>(serverName);
            _chatMessages = new ReplaySubject<LobbyChatMessage>();

            //First init just ourselves.
            List<LobbyPlayerInfo> initialLobbyPlayerInfos = new List<LobbyPlayerInfo>()
            {
                new LobbyPlayerInfo(hostPlayerName, "Team 1", EPlayerType.Local)
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

        private void OnReceiveNewClientGreeting(NewLobbyClientGreeting greeting)
        {
            Debug.WriteLine($"Received greeting from new client: {greeting.PlayerName}");

            //Send the server name.
            LobbyServerNameMessage lobbyServerNameMessage = new LobbyServerNameMessage(_serverName.Value);
            _commandDispatcher.SendCommandAsync(lobbyServerNameMessage);

            //TODO: Have something behind the player info list instead of doing this.
            int tempTeamNumber = _playerInfos.Value.Count + 1;

            List<LobbyPlayerInfo> playerInfos = new List<LobbyPlayerInfo>(_playerInfos.Value)
            {
                new LobbyPlayerInfo(greeting.PlayerName, $"Team {tempTeamNumber}", EPlayerType.Network)
            };

            LobbyPlayerListUpdate playerListUpdateMessage = new LobbyPlayerListUpdate(playerInfos);
            _commandDispatcher.SendCommandAsync(playerListUpdateMessage);

            _playerInfos.OnNext(playerInfos);
        }

        private void OnClientDisconnected(ConnectionID disconnectedClientID)
        {
            //TODO.
        }

        private void OnChatMessageReceived(LobbyChatMessage chatMessage)
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
            //If we ever require readying up, this is where that can go.
            Launch();
            failReason = null;
            return true;
        }

        private void Launch()
        {

        }
    }
}
