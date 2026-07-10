using FDG.Data;
using FDG.StageResolution;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// The Tactician's registry (#191 A4): its own resolvers where a slice has replaced one, the
    /// unmodified solo-rules registry for everything else. A delegating wrapper rather than
    /// re-registration because the base registry's JSON delegate map rejects duplicate keys - and
    /// because falling through to the INNER registry means new solo-rules request types are handled
    /// automatically (G3: the Tactician must never fault a stage it has no resolver for).
    /// </summary>
    public sealed class TacticianRegistry : IStageResolverRegistry
    {
        private readonly IStageResolverRegistry _own = new StageResolverRegistry();
        private readonly HashSet<string> _ownTypes = new();
        private readonly IStageResolverRegistry _soloRules;

        public TacticianRegistry(IStageResolverRegistry soloRules)
        {
            _soloRules = soloRules;
        }

        /// <summary>Registers a Tactician resolver; requests of this type no longer reach solo-rules.</summary>
        public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
            where TRequest : IStageTaskRequest<TReply>
        {
            _own.RegisterResolver(resolver);
            _ownTypes.Add(typeof(TRequest).FullName!);
            return this;
        }

        public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
            => _ownTypes.Contains(typeof(TRequest).FullName!)
                ? _own.ResolveRequest<TRequest, TReply>(request)
                : _soloRules.ResolveRequest<TRequest, TReply>(request);

        public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson,
            IReadableGameDataStore gameDataStore)
            => _ownTypes.Contains(typeFullName)
                ? _own.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore)
                : _soloRules.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
    }
}
