
using FDG;

namespace FutureOfDarkGrimness.TextInterface
{
    public interface IPlayerTextRelayer
    {
        void SendLogMessageToAll(string message);

        void SendGlobalPlayerMessage(PlayerID sendingPlayer, string message);

        void SendTeamPlayerMessage(PlayerID sendingPlayer, string message);
    }
}
