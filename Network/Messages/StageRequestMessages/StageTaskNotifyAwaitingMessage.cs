using FDG.Players;
using FDG.StageResolution;


namespace FDG.Network.Messages.StageRequestMessages
{
    // Carries a PlayerSlotInfo value snapshot rather than a DataBinding<PlayerSlotInfo>: the receive side
    // (OutstandingTaskLister) only needs to display who we're waiting on, and a live binding forced the
    // slot's store entry to already exist client-side at deserialize time — fragile if message order ever
    // changed. A by-value snapshot has no such dependency (#088).
    public record StageTaskNotifyAwaitingMessage(TaskID TaskID, PlayerSlotInfo PlayerInfo, string UserFriendlyTaskName);
}
