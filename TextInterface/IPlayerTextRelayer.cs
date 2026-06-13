
using FDG;

namespace FDG.TextInterface
{
    public interface IPlayerTextRelayer
    {
        void SendLogMessageToAll(string message, TextColor color);
    }
}
