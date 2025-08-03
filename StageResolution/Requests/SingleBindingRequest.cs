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
        public string Instructions { get; }

        [JsonConstructor]
        public SingleBindingRequest(PlayerID targetPlayerID, TaskID taskID,string instructions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            TaskName = "Select Item";
        }

        public SingleBindingRequest(PlayerID targetPlayerID, string instructions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            Instructions = instructions;
            TaskName = "Select Item";
        }

        public Task<DataBinding<T>> Resolve(DataBinding<T> resolution)
        {
            return Task.FromResult(resolution);
        }
    }
} 