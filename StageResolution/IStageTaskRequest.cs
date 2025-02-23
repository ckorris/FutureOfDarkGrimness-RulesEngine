
namespace FDG.StageResolution
{
    public interface IStageTaskRequest<TReply> : IStageTaskRequest
    {
        Task<TReply> Resolve(TReply resolution);
    }

    public interface IStageTaskRequest
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }
    }
}
