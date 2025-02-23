
namespace FDG.StageResolution
{
    public interface IStageResolverRegistry
    {
        IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
            where TRequest : IStageTaskRequest<TReply>;
    }
}
