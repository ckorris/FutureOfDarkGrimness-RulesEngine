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
        public IObservable<string> ServerName => _serverName;

        public IObservable<LobbyChatMessage> ChatMessages => _chatMessages;

        public IObservable<IReadOnlyList<LobbyPlayerInfo>> PlayerInfos => _playerInfos;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessages;

        private BehaviorSubject<IReadOnlyList<LobbyPlayerInfo>> _playerInfos;

        private FDGHost _host;

        private string _hostPlayerName;

        private ICommandDispatcher _commandDispatcher;
        private MessageSerializer _messageSerializer;

        private const string SERVER_START_MESSAGE = "Server started successfully.";

        public LobbyViewModel_Host(string hostPlayerName, string serverName, string? password, FDGHost host)
        {
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

            _messageSerializer = new MessageSerializer();
            host.OnNewClientConnected += OnNewClientConnected;
            host.OnClientDisconnected += OnClientDisconnected;
            _commandDispatcher.OnCommandReceived += _messageSerializer.DeserializeMessageAndInvoke;

            _messageSerializer.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _messageSerializer.RegisterForMessageEvent<NewLobbyClientGreeting>(OnReceiveNewClientGreeting);

            //Show init message in chatbox.
            _chatMessages.OnNext(new LobbyChatMessage("System", SERVER_START_MESSAGE));
        }


        public void SendMessage(string message)
        {
            LobbyChatMessage chatMessage = new LobbyChatMessage(_hostPlayerName, message);

            ArraySegment<byte> messageBytes = _messageSerializer.SerializeMessage(chatMessage);
            _commandDispatcher.SendCommandAsync(messageBytes);

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
            ArraySegment<byte> lobbyServerNameBytes = _messageSerializer.SerializeMessage(lobbyServerNameMessage);
            _commandDispatcher.SendCommandAsync(lobbyServerNameBytes);

            //TODO: Have something behind the player info list instead of doing this.
            int tempTeamNumber = _playerInfos.Value.Count + 1;

            List<LobbyPlayerInfo> playerInfos = new List<LobbyPlayerInfo>(_playerInfos.Value);
            playerInfos.Add(new LobbyPlayerInfo(greeting.PlayerName, $"Team {tempTeamNumber}", EPlayerType.Network));

            LobbyPlayerListUpdate playerListUpdateMessage = new LobbyPlayerListUpdate(playerInfos);
            ArraySegment<byte> updateBytes = _messageSerializer.SerializeMessage(playerListUpdateMessage);
            _commandDispatcher.SendCommandAsync(updateBytes);

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
            ArraySegment<byte> messageBytes = _messageSerializer.SerializeMessage(chatMessage);
            _commandDispatcher.SendCommandAsync(messageBytes); //TODO: Release the byte array but gotta be careful on timing.

            //Put the chat message in our own box.
            _chatMessages.OnNext(chatMessage);
        }

        public void Dispose()
        {
            _messageSerializer.DeregisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _host.OnNewClientConnected -= OnNewClientConnected;
            _host.OnClientDisconnected -= OnClientDisconnected;
        }
    }
}
