using FDG.StageResolution;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// A request that presents a yes/no question to the player.
    /// </summary>
    public class YesNoRequest : IStageTaskRequest<bool>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string QuestionText { get; }

        public YesNoRequest(PlayerID targetPlayerID, TaskID taskID, string questionText)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            QuestionText = questionText;
            TaskName = "Yes/No Question";
        }

        public Task<bool> Resolve(bool resolution)
        {
            return Task.FromResult(resolution);
        }
    }
} 