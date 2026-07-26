namespace FDG.Network.Messages.StagePreviewMessages
{
    /// <summary>
    /// Host -> all: the authoritative form of a player's live decision preview (#280), either
    /// re-broadcast by the host's <c>PreviewRelayer</c> from a validated remote
    /// <see cref="SubmitStagePreviewMessage"/> or broadcast directly by a host-local player's
    /// channel. Consumed by each client's <c>PreviewFeed</c>, latest-wins per (player, slot).
    /// </summary>
    public record StagePreviewMessage(PlayerID SourcePlayerID, string Slot,
        string PreviewTypeName, string PreviewJson);
}
