using FDG.Players;

namespace FDG.Network.Messages
{
    /// <summary>
    /// Used when a network player wants to send a chat message. This goes to the server, 
    /// which then relays it as a different message.
    /// </summary>
    public record NetworkPlayerSubmitChatMessage(EChatMessageType MessageType, string Message);
}
