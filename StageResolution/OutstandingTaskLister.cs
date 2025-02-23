using System.Reactive.Subjects;

namespace FDG.StageResolution
{
    internal class OutstandingTaskLister : IOutstandingTaskLister
    {
        public IObservable<IReadOnlyCollection<OutstandingTaskInfo>> OutstandingTasks
            => _outstandingTasks;

        private BehaviorSubject<IReadOnlyCollection<OutstandingTaskInfo>> _outstandingTasks;

        private Dictionary<TaskID, OutstandingTaskInfo> _outstandingTaskInfos;

        public OutstandingTaskLister()
        {
            _outstandingTaskInfos = new Dictionary<TaskID, OutstandingTaskInfo>();
            _outstandingTasks = new BehaviorSubject<IReadOnlyCollection<OutstandingTaskInfo>>(_outstandingTaskInfos.Values);
        }

        public void NotifyTaskRequested(PlayerID targetPlayerID, TaskID taskID, string taskName)
        {
            _outstandingTaskInfos.Add(taskID, new OutstandingTaskInfo(targetPlayerID, taskName));
            _outstandingTasks.OnNext(_outstandingTaskInfos.Values);
        }

        public void NotifyTaskResolved(TaskID taskID)
        {
            _outstandingTaskInfos.Remove(taskID);
            _outstandingTasks.OnNext(_outstandingTaskInfos.Values);
        }

    }
}
