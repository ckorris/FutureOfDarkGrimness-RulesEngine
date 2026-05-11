using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// Selection request whose UI exposes a Back option. Reply is
    /// CancellableResult&lt;DataBinding&lt;T&gt;&gt; — Selected&lt;...&gt; with the chosen binding,
    /// or Cancelled&lt;...&gt; if the player backs out. Use plain
    /// <see cref="SelectionRequest{T}"/> for selections that must complete.
    /// </summary>
    /// <typeparam name="T">The type of data to select from. Must be registered in GameDataStore.</typeparam>
    public class CancellableSelectionRequest<T> : IStageTaskRequest<CancellableResult<DataBinding<T>>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string Instructions { get; }
        public IReadOnlyList<ValidOption> ValidOptions { get; }
        public IReadOnlyList<InvalidOption> InvalidOptions { get; }

        [JsonConstructor]
        public CancellableSelectionRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            TaskName = $"Select {typeof(T).Name}";
        }

        public CancellableSelectionRequest(PlayerID targetPlayerID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), instructions, validOptions, invalidOptions)
        {
        }

        public Task<CancellableResult<DataBinding<T>>> Resolve(CancellableResult<DataBinding<T>> resolution)
        {
            return Task.FromResult(resolution);
        }

        public record ValidOption(DataBinding<T> Option, string Name);
        public record InvalidOption(DataBinding<T> Option, string Name, string Reason);
    }
}
