using FDG.Presentation;
using FDG.StageResolution;

namespace FDG.Players
{
    public interface IPlayerController : IPlayerInfo
    {
        public bool IsReady { get; }

        public event Action<bool> OnReadyStateChanged;

        public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer;

        /// <summary>
        /// This player's presentation-beat consumer. Local players expose their front-end's
        /// sink; remote players get a <see cref="NetworkedPresentationSink"/> that forwards
        /// beats over the bus; AI players have none. Mirrors <see cref="TempVisualDrawer"/>.
        /// </summary>
        public IPresentationSink? PresentationSink { get; }

        public Task WaitUntilReadyAsync();

        public void SendLogMessage(string logMessage, TextColor color);

        // Debug-category variant. Defaults to a normal log line for controllers that don't distinguish
        // (AI, test doubles); the local and network controllers override it to route the debug flag on.
        public void SendLogMessage(string logMessage, TextColor color, bool isDebug)
            => SendLogMessage(logMessage, color);

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message);
    }
}
