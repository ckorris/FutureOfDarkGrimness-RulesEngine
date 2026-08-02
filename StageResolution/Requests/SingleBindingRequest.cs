using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// A request that prompts the player to select a single DataBinding<T> without presenting a list of options.
    /// </summary>
    public class SingleBindingRequest<T> : IStageTaskRequest<DataBinding<T>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }
        public string Instructions { get; }

        // displayName: game wording for the #318 "Waiting on" HUD line.
        [JsonConstructor]
        public SingleBindingRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            string? displayName = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            TaskName = "Select Item";
            DisplayName = displayName ?? TaskName;
        }

        public SingleBindingRequest(PlayerID targetPlayerID, string instructions,
            string? displayName = null)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), instructions, displayName)
        {
        }

        public Task<DataBinding<T>> Resolve(DataBinding<T> resolution)
        {
            return Task.FromResult(resolution);
        }
    }
} 