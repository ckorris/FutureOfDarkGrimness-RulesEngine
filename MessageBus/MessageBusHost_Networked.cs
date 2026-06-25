using FDG.Data;
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

        // Forwarded straight from the network host so host-side consumers can react to a dropped client
        // without depending on INetworkHost directly (#076).
        public event Action<ConnectionID>? OnClientDisconnected;

        internal MessageBusHost_Networked(INetworkHost networkHost, IReadableGameDataStore gameDataStore)
        {
            _networkHost = networkHost;
            _messageRegistrar = new MessageRegistrar();
            _messageSerializer = new MessageSerializer(gameDataStore);
            _networkHost.OnMessageReceived += OnMessageBytesReceived;
            _networkHost.OnClientDisconnected += connectionID => OnClientDisconnected?.Invoke(connectionID);
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

        public void RegisterForConnectionMessageEvent<T>(Action<T, ConnectionID> onMessageReceived)
        {
            _messageRegistrar.RegisterForConnectionMessageEvent(onMessageReceived);
            _messageSerializer.RegisterMessageType<T>();
        }

        public void DeregisterForConnectionMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe)
        {
            _messageRegistrar.DeregisterForConnectionMessageEvent(messageToUnsubscribe);
        }

        public Task SendCommandToAllAsync<TMessage>(TMessage message)
        {
            ArraySegment<Byte> messageBytes = _messageSerializer.SerializeMessage(message);
            _messageRegistrar.DispatchToHandlers(message); //For local player.
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

                //Thread the source connection through dispatch so connection-aware handlers reply to the
                //right sender even when multiple client read loops dispatch concurrently.
                _messageRegistrar.DispatchToHandlers(message, connectionID);
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
