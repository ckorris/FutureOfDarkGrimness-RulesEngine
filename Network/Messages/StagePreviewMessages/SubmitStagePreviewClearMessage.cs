namespace FDG.Network.Messages.StagePreviewMessages
{
    /// <summary>
    /// Client -> host: the sending player's pending request ended (resolved or backed out), drop
    /// every preview slot published under their name (#277). Relay counterpart of
    /// <see cref="SubmitStagePreviewMessage"/>; re-broadcast as <see cref="StagePreviewClearMessage"/>.
    /// </summary>
    public record SubmitStagePreviewClearMessage(PlayerID SourcePlayerID);
}
