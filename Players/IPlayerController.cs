using FDG.StageResolution;

namespace FDG.Players
{
    internal interface IPlayerController : IPlayerInfo
    {
        public bool IsReady { get; }

        public event Action<bool> OnReadyStateChanged;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>;

        public Task WaitUntilReadyAsync();

        public void SendLogMessage(string logMessage);

        public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message);
    }
}
