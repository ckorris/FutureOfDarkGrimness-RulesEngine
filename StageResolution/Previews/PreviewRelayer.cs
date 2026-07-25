using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.StagePreviewMessages;
using FDG.Players;

namespace FDG.StageResolution.Previews
{
    /// <summary>
    /// Host-side relay for remote clients' preview submissions (#277): validates that the sending
    /// connection actually owns the claimed player (a client cannot draw ghosts in another player's
    /// name), applies flood and size caps (the stream is mouse-driven and a broadcast amplifies to
    /// every client), then re-broadcasts as the authoritative <see cref="StagePreviewMessage"/>.
    ///
    /// <para>
    /// Registered connection-aware only, so locally-dispatched messages (no connection) never reach
    /// it - host-local players broadcast directly via <see cref="PreviewChannel"/> and have no
    /// business submitting. The payload JSON is re-broadcast untouched, never deserialized here:
    /// a malicious body can only be as dangerous as each receiver's own allowlisted parse (#186).
    /// Previews are cosmetic, so every drop path is silent-but-logged-once rather than fatal: a
    /// dropped update costs one stale frame, and a dropped clear is mopped up by the feed's
    /// last-task-resolved expiry.
    /// </para>
    /// </summary>
    internal class PreviewRelayer : IDisposable
    {
        // Publisher cadence is ~10 Hz across a couple of slots; 40/s leaves honest headroom while
        // keeping a hostile client from saturating the broadcast path. Window resets each second.
        internal const int MAX_MESSAGES_PER_SECOND_PER_CONNECTION = 40;

        // Generous for the movement family's worst case (a large unit's base slot with several
        // waypoints per model is ~a few KB); anything near this is malformed or hostile.
        internal const int MAX_PREVIEW_JSON_CHARS = 32_000;

        private readonly IMessageBusHost _messageBusHost;
        private readonly PlayerSlotManager _playerSlotManager;
        private readonly ITextOutput _textOutput;

        // Handlers run concurrently on per-client read loops - one lock guards both dictionaries.
        private readonly object _lock = new object();
        private readonly Dictionary<ConnectionID, RateWindow> _rateWindows
            = new Dictionary<ConnectionID, RateWindow>();
        private readonly HashSet<ConnectionID> _warnedConnections = new HashSet<ConnectionID>();

        private class RateWindow
        {
            public long WindowStartMs;
            public int Count;
        }

        public PreviewRelayer(IMessageBusHost messageBusHost, PlayerSlotManager playerSlotManager,
            ITextOutput textOutput)
        {
            _messageBusHost = messageBusHost;
            _playerSlotManager = playerSlotManager;
            _textOutput = textOutput;

            _messageBusHost.RegisterForConnectionMessageEvent<SubmitStagePreviewMessage>(OnSubmitUpdate);
            _messageBusHost.RegisterForConnectionMessageEvent<SubmitStagePreviewClearMessage>(OnSubmitClear);
        }

        private void OnSubmitUpdate(SubmitStagePreviewMessage message, ConnectionID connectionID)
        {
            if (ValidateSender(message.SourcePlayerID, connectionID) == false)
            {
                return;
            }

            if (string.IsNullOrEmpty(message.Slot) || string.IsNullOrEmpty(message.PreviewTypeName)
                || message.PreviewJson == null || message.PreviewJson.Length > MAX_PREVIEW_JSON_CHARS)
            {
                WarnOnce(connectionID, "sent a malformed or oversized preview payload");
                return;
            }

            if (TryTakeRateToken(connectionID) == false)
            {
                return;
            }

            _ = _messageBusHost.SendCommandToAllAsync(new StagePreviewMessage(message.SourcePlayerID,
                message.Slot, message.PreviewTypeName, message.PreviewJson));
        }

        private void OnSubmitClear(SubmitStagePreviewClearMessage message, ConnectionID connectionID)
        {
            if (ValidateSender(message.SourcePlayerID, connectionID) == false)
            {
                return;
            }

            // Clears share the update window: they amplify to every client just the same, and a
            // legitimate clear starved by a flood is covered by the feed's resolved-task expiry.
            if (TryTakeRateToken(connectionID) == false)
            {
                return;
            }

            _ = _messageBusHost.SendCommandToAllAsync(new StagePreviewClearMessage(message.SourcePlayerID));
        }

        private bool ValidateSender(PlayerID claimedPlayerID, ConnectionID connectionID)
        {
            if (_playerSlotManager.TryGetPlayerIDByConnection(connectionID, out PlayerID actualPlayerID) == false)
            {
                WarnOnce(connectionID, "submitted a preview but has no player slot");
                return false;
            }

            if (actualPlayerID != claimedPlayerID)
            {
                WarnOnce(connectionID, $"submitted a preview claiming player {claimedPlayerID} " +
                    $"but owns player {actualPlayerID}");
                return false;
            }

            return true;
        }

        private bool TryTakeRateToken(ConnectionID connectionID)
        {
            long nowMs = Environment.TickCount64;

            lock (_lock)
            {
                if (_rateWindows.TryGetValue(connectionID, out RateWindow? window) == false)
                {
                    window = new RateWindow { WindowStartMs = nowMs };
                    _rateWindows[connectionID] = window;
                }

                if (nowMs - window.WindowStartMs >= 1000)
                {
                    window.WindowStartMs = nowMs;
                    window.Count = 0;
                }

                window.Count++;

                if (window.Count > MAX_MESSAGES_PER_SECOND_PER_CONNECTION)
                {
                    WarnOnce(connectionID, "exceeded the preview rate cap; dropping excess");
                    return false;
                }

                return true;
            }
        }

        private void WarnOnce(ConnectionID connectionID, string what)
        {
            lock (_lock)
            {
                if (_warnedConnections.Add(connectionID) == false)
                {
                    return;
                }
            }

            _textOutput.Log($"Preview relay: connection {connectionID.ID} {what}. " +
                "Further warnings from this connection suppressed.");
        }

        public void Dispose()
        {
            _messageBusHost.DeregisterForConnectionMessageEvent<SubmitStagePreviewMessage>(OnSubmitUpdate);
            _messageBusHost.DeregisterForConnectionMessageEvent<SubmitStagePreviewClearMessage>(OnSubmitClear);
        }
    }
}
