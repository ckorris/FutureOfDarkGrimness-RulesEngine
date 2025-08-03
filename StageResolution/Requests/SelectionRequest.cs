using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// A request that presents a list of options to the player and allows them to select one.
    /// </summary>
    /// <typeparam name="T">The type of data to select from. Must be a type registered in GameDataStore.</typeparam>
    public class SelectionRequest<T> : IStageTaskRequest<DataBinding<T>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string Instructions { get; }
        public IReadOnlyList<ValidOption> ValidOptions { get; }
        public IReadOnlyList<InvalidOption> InvalidOptions { get; }

        [JsonConstructor]
        public SelectionRequest(PlayerID targetPlayerID, TaskID taskID, string taskName, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            TaskName = $"Select {typeof(T).Name}";
        }

        public SelectionRequest(PlayerID targetPlayerID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            TaskName = $"Select {typeof(T).Name}";
        }

        public Task<DataBinding<T>> Resolve(DataBinding<T> resolution)
        {
            return Task.FromResult(resolution);
        }

        public record ValidOption(DataBinding<T> Option, string Name);
        public record InvalidOption(DataBinding<T> Option, string Name, string Reason);
    }
} 