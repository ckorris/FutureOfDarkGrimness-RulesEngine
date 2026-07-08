
using FDG;

namespace FDG.TextInterface
{
    public interface IPlayerTextRelayer
    {
        // isDebug marks a developer-facing line so front ends can route it to a hidden-by-default Debug view.
        void SendLogMessageToAll(string message, TextColor color, bool isDebug = false);
    }
}
