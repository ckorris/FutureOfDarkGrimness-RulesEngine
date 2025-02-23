using FDG.StageResolution;

namespace FDG.Network.Messages.StageRequestMessages
{
    public record StageTaskReplyMessage(PlayerID PlayerID, TaskID TaskID,
        string ReplyFullTypeName, string ReplyJson);
}
