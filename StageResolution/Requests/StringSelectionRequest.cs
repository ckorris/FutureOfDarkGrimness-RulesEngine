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

        /// <summary>
        /// Optional secondary text shown under a valid option, keyed by the option string (e.g. a spell's
        /// effect summary under its name). Options absent from the map render with no subtext; null when no
        /// option has a description (the common case — the action menu, custom actions, etc.).
        /// </summary>
        public Dictionary<string, string>? OptionDescriptions { get; }

        [JsonConstructor]
        public StringSelectionRequest(PlayerID targetPlayerID, TaskID taskID,
            string instructions, IReadOnlyList<string> validOptions, IReadOnlyList<InvalidOption> invalidOptions,
            Dictionary<string, string>? optionDescriptions = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            OptionDescriptions = optionDescriptions;
            TaskName = "Select Option";
        }

        public StringSelectionRequest(PlayerID targetPlayerID, string instructions,
            IReadOnlyList<string> validOptions, IReadOnlyList<InvalidOption> invalidOptions,
            Dictionary<string, string>? optionDescriptions = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            OptionDescriptions = optionDescriptions;
            TaskName = "Select Option";
        }

        public Task<string> Resolve(string resolution)
        {
            return Task.FromResult(resolution);
        }
    }
}
