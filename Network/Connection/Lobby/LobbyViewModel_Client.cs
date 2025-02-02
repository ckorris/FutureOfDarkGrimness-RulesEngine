using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
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

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessages;

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

            _commandDispatcher = client;

            _messageSerializer = new MessageSerializer();
            _messageSerializer.RegisterMessageType<LobbyChatMessage>();
            _commandDispatcher.OnCommandReceived += _messageSerializer.DeserializeMessageAndInvoke;

            _messageSerializer.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
        }

        private void OnChatMessageReceived(LobbyChatMessage message)
        {
            Debug.WriteLine($"Received chat message as client: {message.Message}");

            _chatMessages.OnNext(message);
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
