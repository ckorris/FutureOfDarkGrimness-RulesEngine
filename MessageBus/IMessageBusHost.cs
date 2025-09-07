using FDG.Network.Connection;

namespace FDG.MessageBus
{
    public interface IMessageBusHost : IMessageReceiver
    {
        public Task SendCommandToAllAsync<TMessage>(TMessage message);

        public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID);

    }
}
