namespace FDG.StageResolution
{
    /// <summary>
    /// Registers a resolver written for a BASE request type as the handler for a DERIVED request
    /// type. The registry dispatches on exact type, so splitting a decision into its own request
    /// subclass (e.g. <see cref="Requests.ChooseUnitToActivateRequest"/>, #191 A4-1) would otherwise
    /// force every resolver set to duplicate its handler; this forwards instead. Forwarding to a
    /// SHARED instance also keeps stateful GUI resolvers working unchanged - the overlay draws the
    /// inner resolver's pending state no matter which request type set it.
    /// </summary>
    public sealed class DerivedRequestAdapter<TDerived, TBase, TReply> : IStageResolver<TDerived, TReply>
        where TDerived : TBase, IStageTaskRequest<TReply>
        where TBase : IStageTaskRequest<TReply>
    {
        private readonly IStageResolver<TBase, TReply> _inner;

        public DerivedRequestAdapter(IStageResolver<TBase, TReply> inner)
        {
            _inner = inner;
        }

        public Task<TReply> Resolve(TDerived request) => _inner.Resolve(request);
    }
}
