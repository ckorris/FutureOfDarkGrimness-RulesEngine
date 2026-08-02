
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

        /// <summary>
        /// Stable identifier for this task. Machine-matched in places (the Tactician resolvers
        /// discriminate placement flavors by it), so treat renames as breaking; for the human-facing
        /// wording, use <see cref="DisplayName"/>.
        /// </summary>
        public string TaskName { get; }

        /// <summary>
        /// What everyone else reads while this task holds up the game - the #318 status HUD shows
        /// "Waiting on Bob: {DisplayName}". Word it as what the player is doing in game terms
        /// ("Deploying Warriors"), not what the code wants from them ("Select UnitData").
        /// Defaults to <see cref="TaskName"/>.
        /// </summary>
        public string DisplayName => TaskName;
    }
}
