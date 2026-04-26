using FDG.StageResolution;
using Newtonsoft.Json;

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

        [JsonConstructor]
        public YesNoRequest(PlayerID targetPlayerID, TaskID taskID, string questionText)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            QuestionText = questionText;
            TaskName = "Yes/No Question";
        }

        public YesNoRequest(PlayerID targetPlayerID,string questionText)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            QuestionText = questionText;
            TaskName = "Yes/No Question";
        }

        public Task<bool> Resolve(bool resolution)
        {
            return Task.FromResult(resolution);
        }
    }
} 