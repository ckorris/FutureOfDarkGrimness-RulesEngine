using FDG.Network.Connection;

namespace FDG.MessageBus
{
    public interface IMessageBusHost : IMessageReceiver
    {
        /// <summary>
        /// This can only be called during the invocation of a method called as a result of 
        /// subscribing to <see cref="IMessageReceiver.RegisterForMessageEvent{T}(Action{T})"/>.
        /// It will return the Connection ID of the message, if there is one, for cases where you need to manage
        /// the network connectivity. There should be very few places to actually use this, it's slightly hacky.
        /// </summary>
        public ConnectionID GetCurrentMessageConnectionID();

        public Task SendCommandToAllAsync<TMessage>(TMessage message);

        public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID);

    }
}
