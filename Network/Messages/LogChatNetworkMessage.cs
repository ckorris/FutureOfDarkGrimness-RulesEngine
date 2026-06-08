
namespace FDG.Network.Messages
{
    /// <summary>
    /// Used for sending log messages to network players, with the text color to display them in.
    /// </summary>
    /// <seealso cref="FDG.Players.NetworkPlayerController"/>
    public record LogChatNetworkMessage(string LogMessage, TextColor Color);
}
