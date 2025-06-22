
namespace FDG.Network.Messages
{
    /// <summary>
    /// Used for sending log messages to network players.
    /// </summary>
    /// <param name="logMessage"></param>
    /// <seealso cref="FDG.Players.NetworkPlayerController"/>
    public record LogChatNetworkMessage(string logMessage);
}
