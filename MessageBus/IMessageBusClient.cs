
namespace FDG.MessageBus
{
    internal interface IMessageBusClient : IMessageReceiver
    {
        public Task SendMessageToHostAsync<TMessage>(TMessage message);

    }
}
