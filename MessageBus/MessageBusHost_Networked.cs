using FDG.Network.Connection;
using FDG.Network.Messages;

namespace FDG.MessageBus
{
    /// <summary>
    /// TODO: This assumes a non-dedicated server where you have a local client.
    /// </summary>
    internal class MessageBusHost_Networked : IMessageBusHost, IMessageBusClient
    {
        INetworkHost _networkHost;
        private IMessageRegistrar _messageRegistrar;
        private IMessageSerializer _messageSerializer;

        private ConnectionID? _lastMessageConnectionID = null;

        internal MessageBusHost_Networked(INetworkHost networkHost)
        {
            _networkHost = networkHost;
            _messageRegistrar = new MessageRegistrar();
            _messageSerializer = new MessageSerializer();
            _networkHost.OnMessageReceived += OnMessageBytesReceived;
        }

        public void RegisterForMessageEvent<T>(Action<T> onMessageReceived)
        {
            _messageRegistrar.RegisterForMessageEvent(onMessageReceived);
            _messageSerializer.RegisterMessageType<T>();
        }

        public void DeregisterForMessageEvent<T>(Action<T> messageToUnsubscribe)
        {
            _messageRegistrar.DeregisterForMessageEvent(messageToUnsubscribe);
        }

        public Task SendCommandToAllAsync<TMessage>(TMessage message)
        {
            ArraySegment<Byte> messageBytes = _messageSerializer.SerializeMessage(message);
            _messageRegistrar.DispatchToHandlers(messageBytes); //For local player.
            return _networkHost.SendCommandToAllAsync(messageBytes, true);
        }

        public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID)
        {
            //TODO: I don't think this should be here because it requires knowledge of connection stuff,
            //but currently I don't have an alternative way to handle one client asking for
            //a data dump, which you definitely don't want to send to all clients.
            ArraySegment<Byte> messageBytes = _messageSerializer.SerializeMessage(message);
            return _networkHost.SendCommandToSingleClientAsync(connectionID, messageBytes, true);
        }

        public Task SendCommandToHostAsync<TMessage>(TMessage message)
        {
            _messageRegistrar.DispatchToHandlers(message);
            return Task.CompletedTask;
        }

        private void OnMessageBytesReceived(ArraySegment<Byte> receivedBytes, ConnectionID connectionID)
        {
            object? message = _messageSerializer.DeserializeMessage(receivedBytes);

            if (message != null)
            {
                System.Diagnostics.Debug.WriteLine($"Client received message: {message.GetType()}");
            }

            //Cache connection ID in case it's needed during invocation.
            _lastMessageConnectionID = connectionID;

            try
            {
                if (message != null)
                {
                    _messageRegistrar.DispatchToHandlers(message);
                }
            }
            finally
            {
                _lastMessageConnectionID = null;
            }
        }

        public void Dispose()
        {
            if (_networkHost != null)
            {
                _networkHost.OnMessageReceived -= OnMessageBytesReceived;
            }
        }

        public ConnectionID GetCurrentMessageConnectionID()
        {
            if(_lastMessageConnectionID.HasValue == false)
            {
                throw new InvalidOperationException($"{nameof(GetCurrentMessageConnectionID)} was called when no {nameof(ConnectionID)} was registered " + 
                    "for a message. It's likely this was called outside the invocation of an event when a message was received.");
            }

            return _lastMessageConnectionID.Value;
        }
    }
}
