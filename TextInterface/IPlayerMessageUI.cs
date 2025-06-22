using FDG.Players;

namespace FDG.TextInterface
{
    public interface IPlayerMessageUI
    {
        event Action<string> OnGlobalMessageRequested;

        event Action<string> OnTeamMessageRequested;

        event Action<PlayerID, string> OnDirectMessageRequested;

        void DisplayPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message);
    }
}
