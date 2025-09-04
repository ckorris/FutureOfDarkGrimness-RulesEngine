using FDG.Network.Connection;
using FDG.Network.Messages;

namespace FDG.MessageBus
{
    internal class MessageBusHost_Networked : IMessageBusHost
    {
        INetworkHost _networkHost;
        private IMessageRegistrar _messageRegistrar;
        private IMessageSerializer _messageSerializer;

        internal MessageBusHost_Networked(INetworkHost networkHost)
        {
            _networkHost = networkHost;
            _messageRegistrar = new MessageRegistrar();
            _messageSerializer = new MessageSerializer();
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
            return _networkHost.SendCommandToAllAsync(messageBytes, true);
        }

        public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID)
        {
            ArraySegment<Byte> messageBytes = _messageSerializer.SerializeMessage(message);
            return _networkHost.SendCommandToSingleClientAsync(connectionID, messageBytes, true);
        }
    }
}
