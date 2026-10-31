using FDG.StageResolution;

namespace FDG.Network.Messages.StageRequestMessages
{
    /// <summary>
    /// Broadcast host -> every client when a decision is over: answered, failed with a resolver error, or
    /// abandoned because its player disconnected. Clears the task from every outstanding-task UI, and since
    /// #451 (replay decision D2) carries the decision itself - the request as sent and the reply as
    /// received - so a client can record every player's decisions in its replay, not only its own.
    /// <para>
    /// This loosens #088's "only the target player sees the payload", but only AFTER the decision has been
    /// acted on, when nothing in it is hidden any more. While the decision is open the request still goes to
    /// the target player alone, and <see cref="StageTaskNotifyAwaitingMessage"/> still carries no payload.
    /// </para>
    /// </summary>
    /// <param name="PlayerID">The player the request was addressed to - the host's own record, not the
    /// replier's claim.</param>
    /// <param name="RequestJson">The request body exactly as it was sent to the target.</param>
    /// <param name="ReplyJson">The reply body exactly as the host received it; null when the request ended
    /// without one.</param>
    /// <param name="ErrorMessage">Why the request ended without a reply (the target's resolver error, or the
    /// disconnect reason); null when it was answered.</param>
    public record StageTaskNotifyResolvedMessage(TaskID TaskID, PlayerID PlayerID, string RequestFullTypeName,
        string ReplyFullTypeName, string DisplayName, string RequestJson, string? ReplyJson, string? ErrorMessage);
}
