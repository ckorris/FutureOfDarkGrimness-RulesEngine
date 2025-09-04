using FDG.Network.Connection;
using FDG.Network.Messages;

namespace FDG.MessageBus
{
    internal class MessageBusClient_Networked : IMessageBusClient
    {
        private INetworkClient _networkClient;
        private IMessageRegistrar _messageRegistrar;
        private IMessageSerializer _messageSerializer;


        internal MessageBusClient_Networked(INetworkClient networkClient)
        {
            _networkClient = networkClient;
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



        public Task SendCommandToHostAsync<TMessage>(TMessage message)
        {
            ArraySegment<Byte> messageBytes = _messageSerializer.SerializeMessage(message);
            return _networkClient.SendCommandToHost(messageBytes, true);
        }

        private void OnMessageBytesReceived(ArraySegment<Byte> receivedBytes)
        {
            object? message = _messageSerializer.DeserializeMessage(receivedBytes);

            if (message != null)
            {
                _messageRegistrar.DispatchToHandlers(message);
            }
        }
    }
}
