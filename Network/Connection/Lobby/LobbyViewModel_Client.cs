using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FutureOfDarkGrimness.Network.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace FutureOfDarkGrimness.Network.Connection.Lobby
{
    public class LobbyViewModel_Client : ILobbyViewModel
    {
        public IObservable<string> ServerName => _serverName;

        public IObservable<LobbyChatMessage> ChatMessages => _chatMessages;

        public IObservable<IReadOnlyList<LobbyPlayerInfo>> PlayerInfos => _playerInfos;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessages;

        private BehaviorSubject<IReadOnlyList<LobbyPlayerInfo>> _playerInfos;

        private string _thisPlayerName;

        private ICommandDispatcher _commandDispatcher;
        private MessageSerializer _messageSerializer;

        private FDGClient _client;

        public LobbyViewModel_Client(string thisPlayerName, FDGClient client)
        {
            _client = client;

            _thisPlayerName = thisPlayerName;

            _serverName = new BehaviorSubject<string>("");
            _chatMessages = new ReplaySubject<LobbyChatMessage>();

            //Init empty player list. The host should update us.
            _playerInfos = new BehaviorSubject<IReadOnlyList<LobbyPlayerInfo>>(new List<LobbyPlayerInfo>());

            _commandDispatcher = client;

            _messageSerializer = new MessageSerializer();
            _commandDispatcher.OnCommandReceived += _messageSerializer.DeserializeMessageAndInvoke;

            _messageSerializer.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
            _messageSerializer.RegisterForMessageEvent<LobbyPlayerListUpdate>(OnPlayerListUpdateReceived);

            //Send greeting. 
            NewLobbyClientGreeting greeting = new NewLobbyClientGreeting(thisPlayerName);
            ArraySegment<byte> greetingBytes = _messageSerializer.SerializeMessage(greeting);
            _commandDispatcher.SendCommandAsync(greetingBytes);
        }

        private void OnChatMessageReceived(LobbyChatMessage message)
        {
            Debug.WriteLine($"Received chat message as client: {message.Message}");

            _chatMessages.OnNext(message);
        }

        private void OnPlayerListUpdateReceived(LobbyPlayerListUpdate playerListUpdate)
        {
            _playerInfos.OnNext(playerListUpdate.PlayerInfoList);
        }

        public void SendMessage(string message)
        {
            Debug.WriteLine($"Sending message: {message}");

            LobbyChatMessage chatMessage = new LobbyChatMessage(_thisPlayerName, message);

            ArraySegment<byte> messageBytes = _messageSerializer.SerializeMessage(chatMessage);
            _commandDispatcher.SendCommandAsync(messageBytes);
        }

        public void Dispose()
        {
            _messageSerializer.DeregisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
        }
    }
}
