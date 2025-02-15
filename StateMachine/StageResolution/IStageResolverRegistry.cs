
namespace FDG.StateMachine.StageResolution
{
    public interface IStageResolverRegistry
    {
        IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
            where TRequest : IStageRequest<TReply>;
    }
}
