using FDG.Data;
using FDG.StageResolution;
using System.Collections.Generic;

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
        public IReadOnlyList<DataBinding<T>> ValidOptions { get; }
        public IReadOnlyDictionary<DataBinding<T>, string> InvalidOptions { get; }

        public SelectionRequest(
            PlayerID targetPlayerID, 
            TaskID taskID, 
            string instructions,
            IReadOnlyList<DataBinding<T>> validOptions,
            IReadOnlyDictionary<DataBinding<T>, string> invalidOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            TaskName = $"Select {typeof(T).Name}";
        }

        public Task<DataBinding<T>> Resolve(DataBinding<T> resolution)
        {
            return Task.FromResult(resolution);
        }
    }
} 