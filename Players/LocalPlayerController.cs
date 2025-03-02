using FDG.StageResolution;

namespace FDG.Players
{
    public class LocalPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        public bool IsReady { get; private set; } = false;

        public event Action<bool>? OnReadyStateChanged;

        private StageResolverRegistry? _stageResolverRegistry = null;

        
        public LocalPlayerController(string name, PlayerID id)
        {
            Name = name;
            ID = id;
        }

        public void AssignStageResolverRegistry(StageResolverRegistry stageResolverRegistry)
        {
            if(_stageResolverRegistry != null)
            {
                throw new InvalidOperationException($"{nameof(StageResolverRegistry)} already assigned.");
            }

            _stageResolverRegistry = stageResolverRegistry;

            IsReady = true;
            OnReadyStateChanged?.Invoke(true);
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply> 
        {
            if(IsReady == false)
            {
                throw new InvalidOperationException($"Tried to request decision of a {nameof(LocalPlayerController)} " + 
                    "that wasn't ready.");
            }

            return _stageResolverRegistry.ResolveRequest<TRequest, TReply>(request);
        }
    }
}
