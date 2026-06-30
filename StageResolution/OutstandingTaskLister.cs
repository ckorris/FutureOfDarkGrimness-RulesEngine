using FDG.MessageBus;
using FDG.Network.Messages.StageRequestMessages;
using System.Reactive.Subjects;

namespace FDG.StageResolution
{
    internal class OutstandingTaskLister : IOutstandingTaskLister, IDisposable
    {
        public IObservable<IReadOnlyCollection<OutstandingTaskInfo>> OutstandingTasks
            => _outstandingTasks;

        private BehaviorSubject<IReadOnlyCollection<OutstandingTaskInfo>> _outstandingTasks;

        private Dictionary<TaskID, OutstandingTaskInfo> _outstandingTaskInfos;

        private IMessageBusClient _messageBusClient;

        public OutstandingTaskLister(IMessageBusClient messageBusClient)
        {
            _outstandingTaskInfos = new Dictionary<TaskID, OutstandingTaskInfo>();
            _outstandingTasks = new BehaviorSubject<IReadOnlyCollection<OutstandingTaskInfo>>(_outstandingTaskInfos.Values);
            _messageBusClient = messageBusClient;

            _messageBusClient.RegisterForMessageEvent<StageTaskNotifyAwaitingMessage>(NotifyTaskRequested);
            _messageBusClient.RegisterForMessageEvent<StageTaskNotifyResolvedMessage>(NotifyTaskResolved);
        }

        public void NotifyTaskRequested(StageTaskNotifyAwaitingMessage awaitingMessage)
        {
            OutstandingTaskInfo taskInfo = new OutstandingTaskInfo(awaitingMessage.PlayerInfo,
                awaitingMessage.UserFriendlyTaskName);
            _outstandingTaskInfos.Add(awaitingMessage.TaskID, taskInfo);
            _outstandingTasks.OnNext(_outstandingTaskInfos.Values);
        }

        public void NotifyTaskResolved(StageTaskNotifyResolvedMessage resolvedMessage)
        {
            _outstandingTaskInfos.Remove(resolvedMessage.TaskID);
            _outstandingTasks.OnNext(_outstandingTaskInfos.Values);
        }

        public void Dispose()
        {
            _messageBusClient?.DeregisterForMessageEvent<StageTaskNotifyAwaitingMessage>(NotifyTaskRequested);
            _messageBusClient?.DeregisterForMessageEvent<StageTaskNotifyResolvedMessage>(NotifyTaskResolved);
        }
    }
}
