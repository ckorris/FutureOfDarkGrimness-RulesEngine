using FDG.Players;
using FDG.StageResolution;

namespace FDG.Tests
{
    internal class NullPlayerRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
            => new TaskCompletionSource<TReply>().Task;
    }
}
