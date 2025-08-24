using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class PlaceObjectsRequest<T> : IStageTaskRequest<Dictionary<DataBinding<T>, Position>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public IReadOnlyList<DataBinding<T>> ModelsToPlace { get; }

        [JsonConstructor]
        public PlaceObjectsRequest(PlayerID targetPlayerID, TaskID taskID, string taskName, 
            IReadOnlyList<DataBinding<T>> objectsToPlace)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            ModelsToPlace = objectsToPlace;
        }

        public PlaceObjectsRequest(PlayerID targetPlayerID, string taskName, 
            IReadOnlyList<DataBinding<T>> objectsToPlace)
        {
            TargetPlayerID = targetPlayerID;
            TaskName = taskName;
            ModelsToPlace = objectsToPlace;
        }

        public Task<Dictionary<DataBinding<T>, Position>> Resolve(Dictionary<DataBinding<T>, Position> resolution)
        {
            return Task.FromResult(resolution);
        }
    }
}
