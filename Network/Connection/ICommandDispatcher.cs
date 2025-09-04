
namespace FDG.Network.Connection
{
    /// <summary>
    /// TODO: Make internal once done prototyping.
    /// </summary>
    public interface ICommandDispatcher
    {
        public void RegisterForMessageEvent<T>(Action<T, ConnectionID> onMessageReceived);

        public void DeregisterForMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe);

        public Task SendCommandToAllAsync<TMessage>(TMessage message);

        public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID);
    }
}
