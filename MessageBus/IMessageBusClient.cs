
namespace FDG.MessageBus
{
    public interface IMessageBusClient : IMessageReceiver
    {
        public Task SendCommandToHostAsync<TMessage>(TMessage message);

    }
}
