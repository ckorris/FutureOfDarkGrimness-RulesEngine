using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Presentation.Messages;

namespace FDG.Presentation
{
    /// <summary>
    /// Per-remote-player <see cref="IPresentationSink"/>: forwards each beat to that one client
    /// over the message bus as a <see cref="PresentBeatMessage"/>. The presentation analogue of
    /// <c>NetworkedTempVisualDrawer</c>.
    ///
    /// <para>
    /// Unlike the temp-visual drawer (which broadcast to all), this targets the specific
    /// connection via <see cref="IMessageBusHost.SendCommandToSingleAsync"/>, so per-player
    /// fan-out from <see cref="PresentationRelayer"/> doesn't duplicate-broadcast when there is
    /// more than one remote client.
    /// </para>
    /// </summary>
    public class NetworkedPresentationSink : IPresentationSink
    {
        private readonly IMessageBusHost _messageBusHost;
        private readonly ConnectionID _connectionID;

        public NetworkedPresentationSink(IMessageBusHost messageBusHost, ConnectionID connectionID)
        {
            _messageBusHost = messageBusHost;
            _connectionID = connectionID;
        }

        public void OnBeat(PresentationBeat beat)
        {
            _messageBusHost.SendCommandToSingleAsync(new PresentBeatMessage(beat), _connectionID);
        }
    }
}
