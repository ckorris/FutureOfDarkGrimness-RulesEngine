

//TODO: Don't think I need this anymore.
/*
namespace FDG.StageResolution
{
    public class IncomingTaskRelayer : IOutstandingTaskLister
    {

        private PlayerID _localPlayerID;

        private StageResolverRegistry _stageResolverRegistry;


        private OutstandingTaskLister _outstandingTaskLister;

        public IncomingTaskRelayer(PlayerID localPlayerID, StageResolverRegistry stageResolverRegistry)
        {
            _localPlayerID = localPlayerID;
            _stageResolverRegistry = stageResolverRegistry;
            _outstandingTaskLister = new OutstandingTaskLister();
        }

        public IObservable<IReadOnlyCollection<OutstandingTaskInfo>> OutstandingTasks => throw new NotImplementedException();

        public void NotifyTaskRequested<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {

            if(request.TargetPlayerID ==  _localPlayerID)
            {
                IStageResolver<TRequest, TReply> resolver = _stageResolverRegistry.GetResolver<TRequest, TReply>();

                resolver.Resolve(request);
            }
        }

        public void NotifyTaskResolved(TaskID taskID)
        {

        }

        

    }
}
*/