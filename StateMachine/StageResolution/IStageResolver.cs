
namespace FDG.StateMachine.StageResolution
{
    public interface IStageResolver<TRequest, TReply>
        where TRequest : IStageRequest<TReply>
    {
        Task<TReply> Resolve(TRequest context);
    }
}
