using FDG.Players;

namespace FDG.TextInterface
{
    public interface IPlayerMessageUI
    {
        event Action<string, EChatMessageType> OnMessageRequested;

        void DisplayPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message);
    }
}