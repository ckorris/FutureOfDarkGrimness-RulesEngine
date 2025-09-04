using FDG.Network.Connection;

namespace FDG.MessageBus
{
    public interface IMessageReceiver
    {
        public void RegisterForMessageEvent<T>(Action<T, ConnectionID> onMessageReceived);

        public void DeregisterForMessageEvent<T>(Action<T, ConnectionID> messageToUnsubscribe);
    }
}
