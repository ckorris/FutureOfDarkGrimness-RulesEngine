using FDG.Network.Connection;

namespace FDG.MessageBus
{
    public interface IMessageBusHost : IMessageReceiver
    {
        /// <summary>
        /// Register a handler that also receives the <see cref="ConnectionID"/> the message arrived on.
        /// Use this instead of an ambient lookup when a handler must reply to the specific sender
        /// (e.g. full-data sync, lobby greeting), so concurrent client read loops can't cross-talk and
        /// hand a handler the wrong connection.
        /// </summary>
        public void RegisterForConnectionMessageEvent<T>(Action<T, ConnectionID> onMessageReceived);

        public void DeregisterForConnectionMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe);

        public Task SendCommandToAllAsync<TMessage>(TMessage message);

        public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID);

    }
}
