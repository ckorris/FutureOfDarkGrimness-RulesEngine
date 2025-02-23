using FDG.StageResolution;


namespace FDG.Network.Messages.StageRequestMessages
{
    public record StageTaskNotifyAwaitingMessage(TaskID TaskID, PlayerID PlayerID, string UserFriendlyTaskName);
}
