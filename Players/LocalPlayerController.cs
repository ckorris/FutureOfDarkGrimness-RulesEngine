using FDG.StageResolution;

namespace FDG.Players
{
    public class LocalPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady { get; private set; } = true;

        public event Action<bool>? OnReadyStateChanged;

        private StageResolverRegistry _stageResolverRegistry;

        
        public LocalPlayerController(string name, PlayerID id, StageResolverRegistry stageResolverRegistry)
        {
            Name = name;
            ID = id;
            _stageResolverRegistry = stageResolverRegistry;
        }

        public Task WaitUntilReadyAsync()
        {
            return this.WaitUntilReadyAsyncStatic();
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply> 
        {
            return _stageResolverRegistry.ResolveRequest<TRequest, TReply>(request);
        }

        
    }
}
