
using FDG.Network.Messages;
using System.Buffers;

namespace FDG.Network.Connection
{
    /// <summary>
    /// TODO: Make internal once done prototyping.
    /// </summary>
    public interface ICommandDispatcher
    {
        public void RegisterForMessageEvent<T>(Action<T> onMessageReceived);

        public void DeregisterForMessageEvent<T>(Action<T> messageToUnsubscribe);

        public Task SendCommandAsync<TMessage>(TMessage message);
    }
}
