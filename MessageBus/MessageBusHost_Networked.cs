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

        internal MessageBusHost_Networked(INetworkHost networkHost)
        {
            _networkHost = networkHost;
            _messageRegistrar = new MessageRegistrar();
            _messageSerializer = new MessageSerializer();
            _networkHost.OnMessageReceived += OnMessageBytesReceived;
        }

        public void RegisterForMessageEvent<T>(Action<T, ConnectionID> onMessageReceived)
        {
            _messageRegistrar.RegisterForMessageEvent(onMessageReceived);
            _messageSerializer.RegisterMessageType<T>();
        }

        public void DeregisterForMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe)
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

        private void OnMessageBytesReceived(ArraySegment<Byte> receivedBytes)
        {
            object? message = _messageSerializer.DeserializeMessage(receivedBytes);

            if (message != null)
            {
                System.Diagnostics.Debug.WriteLine($"Client received message: {message.GetType()}");
            }

            if (message != null)
            {
                _messageRegistrar.DispatchToHandlers(message);
            }
        }

        public void Dispose()
        {
            if (_networkHost != null)
            {
                _networkHost.OnMessageReceived -= OnMessageBytesReceived;
            }
        }
    }
}
