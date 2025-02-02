using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Connection
{
    public class LobbyViewModel_Host : ILobbyViewModel
    {
        public IObservable<string> ServerName => _serverName;

        public IObservable<LobbyChatMessage> ChatMessages => _chatMessages;

        private BehaviorSubject<string> _serverName;

        private ReplaySubject<LobbyChatMessage> _chatMessages;

        private FDGHost _host;

        private string _hostPlayerName;

        private ICommandDispatcher _commandDispatcher;
        private MessageSerializer _messageSerializer;

        public LobbyViewModel_Host(string hostPlayerName, string serverName, string? password, FDGHost host)
        {
            _hostPlayerName = hostPlayerName;

            _serverName = new BehaviorSubject<string>(serverName);
            _chatMessages = new ReplaySubject<LobbyChatMessage>();

            _commandDispatcher = host;

            _messageSerializer = new MessageSerializer();
            _messageSerializer.RegisterMessageType<LobbyChatMessage>();
            _commandDispatcher.OnCommandReceived += _messageSerializer.DeserializeMessageAndInvoke;

            _messageSerializer.RegisterForMessageEvent<LobbyChatMessage>(OnChatMessageReceived);
        }


        public void SendMessage(string message)
        {
            LobbyChatMessage chatMessage = new LobbyChatMessage(_hostPlayerName, message);

            ArraySegment<byte> messageBytes = _messageSerializer.SerializeMessage(chatMessage);
            _commandDispatcher.SendCommandAsync(messageBytes);

            _chatMessages.OnNext(chatMessage);
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
        }
    }
}
