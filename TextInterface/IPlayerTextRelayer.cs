
using FDG;

namespace FutureOfDarkGrimness.TextInterface
{
    public interface IPlayerTextRelayer
    {
        void SendLogMessageToAll(string message, TextColor color);
    }
}
