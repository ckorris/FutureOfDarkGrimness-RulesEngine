using FDG.Players;

namespace FDG.Network.Messages
{
    /// <summary>
    /// Used for sending player messages to/from network players.
    /// </summary>
    /// <seealso cref="NetworkPlayerController"/>
    public record PlayerChatNetworkMessage(string SendingPlayerName, EChatMessageType MessageType,
        string Message);
}
