namespace FDG.Network.Messages.StagePreviewMessages
{
    /// <summary>
    /// Host -> all: drop every preview slot held for this player (#280). Broadcast counterpart of
    /// <see cref="SubmitStagePreviewClearMessage"/>.
    /// </summary>
    public record StagePreviewClearMessage(PlayerID SourcePlayerID);
}
