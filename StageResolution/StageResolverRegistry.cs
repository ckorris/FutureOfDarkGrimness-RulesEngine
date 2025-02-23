using FDG.Network;
using static FDG.StageHandlerRegistry;

namespace FDG.StageResolution
{
    public class StageResolverRegistry : IStageResolverRegistry
    {
        private Dictionary<Type, object> _resolversByRequestType = new Dictionary<Type, object>();

        private WhitelistedTypeDeserializer _typeDeserializer = new WhitelistedTypeDeserializer();

        public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver) 
            where TRequest : IStageTaskRequest<TReply>
        {
            Type requestType = typeof(TRequest);
            AssertHandlerTypeNotYetAdded(requestType);

            _resolversByRequestType[requestType] = resolver;

            //Whitelist and add callback for receiving serialized requests.
            _typeDeserializer.RegisterType<TRequest>(ResolveRequest<TRequest, TReply>);

            return this;
        }

        public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            IStageResolver<TRequest, TReply> resolver = GetResolver<TRequest, TReply>();
            return resolver.Resolve(request);
        }

        public IStageResolver<TRequest, TReply> GetResolver<TRequest, TReply>()
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
