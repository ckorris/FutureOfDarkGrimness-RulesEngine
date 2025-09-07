
namespace FDG.MessageBus
{
    public interface IMessageReceiver : IDisposable
    {
        public void RegisterForMessageEvent<T>(Action<T> onMessageReceived);

        public void DeregisterForMessageEvent<T>(Action<T> messageToUnsubscribe);
    }
}
