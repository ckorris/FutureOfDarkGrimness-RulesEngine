namespace FDG.Network.Messages.StagePreviewMessages
{
    /// <summary>
    /// Client -> host: a remote player's live decision preview (#280) - the transient state their
    /// active resolver is showing (ghost models, planned paths), shared so the other players watch
    /// the move take shape instead of a frozen board. The payload is an opaque (type name, JSON)
    /// pair: the engine transports it; only the front end knows how to build or draw one.
    ///
    /// <para>
    /// <c>Slot</c> subdivides one player's preview into independently-updatable parts so publishers
    /// can split rarely-changing state from the mouse-driven stream (e.g. a movement "base" slot
    /// carrying committed waypoints at click cadence and a "ghost" slot streaming at ~10 Hz) -
    /// receivers cache latest-wins per (player, slot).
    /// </para>
    ///
    /// <para>
    /// Distinct from the host -> all <see cref="StagePreviewMessage"/> (the chat-relay pattern:
    /// submit in, broadcast out) so the host's own re-broadcast dispatch can't re-enter the relay.
    /// Host-local players never send this - their channel broadcasts directly.
    /// </para>
    /// </summary>
    public record SubmitStagePreviewMessage(PlayerID SourcePlayerID, string Slot,
        string PreviewTypeName, string PreviewJson);
}
