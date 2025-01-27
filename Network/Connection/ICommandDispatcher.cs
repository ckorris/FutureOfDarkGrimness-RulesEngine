
namespace FDG.Network.Connection
{
    /// <summary>
    /// TODO: Make internal once done prototyping.
    /// </summary>
    public interface ICommandDispatcher
    {
        public event Action<ArraySegment<byte>> OnCommandReceived;

        public Task SendCommandAsync(ArraySegment<byte> commandBytes);
    }
}
