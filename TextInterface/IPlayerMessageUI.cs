using FDG.Players;

namespace FDG.TextInterface
{
    public interface IPlayerMessageUI
    {
        event Action<string, EChatMessageType> OnMessageSentByPlayer;

        void DisplayPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message);
    }
}