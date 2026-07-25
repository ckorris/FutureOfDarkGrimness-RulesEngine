using FDG.MessageBus;
using FDG.Network.Messages.StagePreviewMessages;

namespace FDG.StageResolution.Previews
{
    /// <summary>
    /// Default <see cref="IPreviewChannel"/>: routes by what the bus can do. A host-capable bus
    /// broadcasts the authoritative <see cref="StagePreviewMessage"/> directly - the host's own
    /// players need no relay hop, and skipping the Submit path there is what lets the relayer
    /// treat every Submit it sees as remote and enforce connection ownership on all of them
    /// (a locally-dispatched Submit would reach the relayer with no connection attached, so a
    /// mixed path couldn't tell host-local from spoofed). A client-only bus sends the Submit
    /// form for the host's <c>PreviewRelayer</c> to validate and re-broadcast.
    ///
    /// <para>Sends are fire-and-forget: previews are cosmetic and superseded ~100 ms later, so
    /// nothing awaits them and a lost send costs one stale frame.</para>
    /// </summary>
    public class PreviewChannel : IPreviewChannel
    {
        private readonly IMessageBusClient _messageBusClient;

        public PreviewChannel(IMessageBusClient messageBusClient)
        {
            _messageBusClient = messageBusClient;
        }

        public void PublishUpdate(PlayerID sourcePlayerID, string slot, string previewTypeName, string previewJson)
        {
            if (_messageBusClient is IMessageBusHost host)
            {
                _ = host.SendCommandToAllAsync(new StagePreviewMessage(sourcePlayerID, slot, previewTypeName, previewJson));
            }
            else
            {
                _ = _messageBusClient.SendCommandToHostAsync(new SubmitStagePreviewMessage(sourcePlayerID, slot, previewTypeName, previewJson));
            }
        }

        public void PublishClear(PlayerID sourcePlayerID)
        {
            if (_messageBusClient is IMessageBusHost host)
            {
                _ = host.SendCommandToAllAsync(new StagePreviewClearMessage(sourcePlayerID));
            }
            else
            {
                _ = _messageBusClient.SendCommandToHostAsync(new SubmitStagePreviewClearMessage(sourcePlayerID));
            }
        }
    }
}
