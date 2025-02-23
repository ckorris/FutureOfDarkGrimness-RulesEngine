
namespace FDG.StageResolution
{
    public interface IStageResolver<TRequest, TReply>
        where TRequest : IStageTaskRequest<TReply>
    {
        Task<TReply> Resolve(TRequest context);
    }
}
