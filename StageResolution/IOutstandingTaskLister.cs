using System.Collections.Generic;

namespace FDG.StageResolution
{
    public interface IOutstandingTaskLister
    {
        public IObservable<IReadOnlyCollection<OutstandingTaskInfo>> OutstandingTasks { get; }
    }
}
