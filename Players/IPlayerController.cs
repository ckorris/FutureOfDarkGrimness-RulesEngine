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

        public void SendLogMessage(string logMessage);

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message);
    }
}
