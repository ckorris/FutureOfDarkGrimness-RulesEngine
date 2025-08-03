using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// A request that presents a list of string options to the player and allows them to select one.
    /// </summary>
    public class StringSelectionRequest : IStageTaskRequest<string>
    {
        public record InvalidOption(string Option, string Reason);

        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string Instructions { get; }
        public IReadOnlyList<string> ValidOptions { get; }
        public IReadOnlyList<InvalidOption> InvalidOptions { get; }

        [JsonConstructor]
        public StringSelectionRequest(PlayerID targetPlayerID, TaskID taskID,
            string instructions, IReadOnlyList<string> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            TaskName = "Select Option";
        }

        public StringSelectionRequest(PlayerID targetPlayerID, string instructions,
            IReadOnlyList<string> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            TaskName = "Select Option";
        }

        public Task<string> Resolve(string resolution)
        {
            return Task.FromResult(resolution);
        }
    }
} 