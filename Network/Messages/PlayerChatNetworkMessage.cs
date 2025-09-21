using FDG.Players;

namespace FDG.Network.Messages
{
    /// <summary>
    /// Used for sending player messages to/from network players when sending to everyone at once.
    /// </summary>
    public record PlayerChatNetworkMessage(PlayerID PlayerID, EChatMessageType MessageType, string Message);
}
