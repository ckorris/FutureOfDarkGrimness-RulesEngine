
namespace FDG.Network.Connection
{
    internal interface ICommandDispatcher
    {
        public event Action<ArraySegment<byte>> OnCommandReceived;

        public Task SendCommandAsync(ArraySegment<byte> commandBytes);
    }
}
