using FDG.Data;
using FDG.Network;
using Newtonsoft.Json;
using static FDG.StageHandlerRegistry;

namespace FDG.StageResolution
{
    public class StageResolverRegistry : IStageResolverRegistry
    {
        private Dictionary<Type, object> _resolversByRequestType = new Dictionary<Type, object>();

        private Dictionary<string, Func<string, IReadableGameDataStore, Task<string>>> _jsonResolverDelegates
            = new Dictionary<string, Func<string, IReadableGameDataStore, Task<string>>>();

        public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
            where TRequest : IStageTaskRequest<TReply>
        {
            Type requestType = typeof(TRequest);
            AssertHandlerTypeNotYetAdded(requestType);

            _resolversByRequestType[requestType] = resolver;

            //Whitelist and add callback for receiving serialized requests.

            string? fullName = requestType.FullName;

            if (fullName == null)
            {
                throw new ArgumentException($"No full name for type {requestType}.");
            }

            _jsonResolverDelegates.Add(key: fullName, value: ResolveRequestAsJson_Typed<TRequest, TReply>);

            return this;
        }

        public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            IStageResolver<TRequest, TReply> resolver = GetResolver<TRequest, TReply>();
            return resolver.Resolve(request);
        }

        public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson, IReadableGameDataStore gameDataStore)
        {
            if (_jsonResolverDelegates.TryGetValue(typeFullName, out Func<string, IReadableGameDataStore, Task<string>>? typedResolveDelegate) == false)
            {
                throw new MissingResolverException($"Requested resolver for request of type {typeFullName}, but it wasn't registered.");
            }

            if (typedResolveDelegate == null)
            {
                throw new NullReferenceException($"Delegate for resolving request type {typeFullName} from Json was null.");
            }

            return typedResolveDelegate(requestJson, gameDataStore);
        }

        private async Task<string> ResolveRequestAsJson_Typed<TRequest, TReply>(string requestJson, IReadableGameDataStore gameDataStore)
            where TRequest : IStageTaskRequest<TReply>
        {
            // This runs on the CLIENT: requestJson arrived from the host over the wire (only
            // NetworkedRequestMessageReceiver calls in here), so with the #264 server browser it may
            // come from a stranger's host. Deserialize through the wire allowlist (#186), not the
            // store's permissive settings. Serialize the reply the same way so the $type BindToName
            // written here always matches what the peer's wire binder will accept on the way back —
            // one source of truth, no drift.
            JsonSerializerSettings wireSettings = WireJsonSettings.For(gameDataStore);
            TRequest? request = JsonConvert.DeserializeObject<TRequest>(requestJson, wireSettings);

            if (request == null)
            {
                throw new JsonSerializationException($"Could not deserialize request into Json.");
            }

            Task<TReply> reply = ResolveRequest<TRequest, TReply>(request);
            await reply;

            // Pass typeof(TReply) so TypeNameHandling.Auto can record the concrete
            // subtype when the declared reply is a polymorphic base
            // (e.g. CancellableResult<T>).
            string replyAsJson = JsonConvert.SerializeObject(reply.Result, typeof(TReply), wireSettings);

            return replyAsJson;
        }

        private IStageResolver<TRequest, TReply> GetResolver<TRequest, TReply>()
            where TRequest : IStageTaskRequest<TReply>
        {
            Type requestType = typeof(TRequest);
            if (_resolversByRequestType.ContainsKey(requestType) == false)
            {
                throw new MissingHandlerException($"Requested resolver for request of type {requestType}, but it wasn't registered.");
            }

            return (IStageResolver<TRequest, TReply>)_resolversByRequestType[requestType];
        }

        private void AssertHandlerTypeNotYetAdded(Type handlerType)
        {
            if (_resolversByRequestType.ContainsKey(handlerType))
            {
                throw new ResolverAlreadyAddedException($"Tried to register handler of type {handlerType} to {nameof(StageHandlerRegistry)}, " +
                    "but it already had one.");
            }
        }

        public class MissingResolverException : Exception
        {
            public MissingResolverException(string message)
                : base(message) { }
        }

        public class ResolverAlreadyAddedException : Exception
        {
            public ResolverAlreadyAddedException(string message)
                : base(message) { }
        }
    }
}
