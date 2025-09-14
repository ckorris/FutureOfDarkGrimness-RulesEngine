using FDG.Data;
using FDG.Players;
using FDG.StageResolution;


namespace FDG.Network.Messages.StageRequestMessages
{
    public record StageTaskNotifyAwaitingMessage(TaskID TaskID, DataBinding<PlayerSlotInfo> PlayerInfo, string UserFriendlyTaskName);
}
