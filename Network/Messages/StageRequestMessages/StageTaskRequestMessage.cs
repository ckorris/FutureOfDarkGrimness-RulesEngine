using FDG.StageResolution;

namespace FDG.Network.Messages.StageRequestMessages
{
    public record StageTaskRequestMessage(PlayerID PlayerID, TaskID TaskID, string RequestFullTypeName,
        string ReplyFullTypeName, string RequestJson);
}
