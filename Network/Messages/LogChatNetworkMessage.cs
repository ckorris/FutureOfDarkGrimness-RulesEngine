
namespace FDG.Network.Messages
{
    /// <summary>
    /// Used for sending log messages to network players, with the text color to display them in.
    /// IsDebug marks a developer-facing line so the client routes it to its Debug view (default false
    /// keeps existing constructions compiling).
    /// </summary>
    /// <seealso cref="FDG.Players.NetworkPlayerController"/>
    public record LogChatNetworkMessage(string LogMessage, TextColor Color, bool IsDebug = false);
}
