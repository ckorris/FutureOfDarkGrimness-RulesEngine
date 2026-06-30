using FDG.Network.Connection;

namespace FDG.MessageBus
{
    public interface IMessageBusHost : IMessageReceiver
    {
        /// <summary>
        /// Raised when a connected client drops. Lets host-side consumers (e.g. the stage-request sender)
        /// react to a departure — failing that player's pending decision requests instead of hanging
        /// forever (#076). Fires on the network read-loop thread; in-process buses never raise it.
        /// </summary>
        public event Action<ConnectionID>? OnClientDisconnected;

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

        /// <summary>
        /// Deliver a message only to in-process handlers on this host (its own local players), with no
        /// network send. Used to route a decision request to a local player without broadcasting it to
        /// every connected client (#088) — the mirror of <see cref="SendCommandToSingleAsync"/> for a
        /// player who has no network connection.
        /// </summary>
        public Task SendCommandToLocalAsync<TMessage>(TMessage message);

    }
}
