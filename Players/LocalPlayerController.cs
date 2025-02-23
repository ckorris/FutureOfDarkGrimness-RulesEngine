using FDG;
using FDG.Players;
using FDG.StageResolution;


namespace FDG.Players
{
    public class LocalPlayerController : IPlayerController
    {
        public string Name { get; }

        public PlayerID ID { get; }

        private StageResolverRegistry _stageResolverRegistry;

        public LocalPlayerController(string name, PlayerID id, StageResolverRegistry stageResolverRegistry)
        {
            Name = name;
            ID = id;
            _stageResolverRegistry = stageResolverRegistry;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply> 
        {
            IStageResolver<TRequest, TReply> resolver = _stageResolverRegistry.GetResolver<TRequest, TReply>();

            return resolver.Resolve(request);
        }
    }
}
