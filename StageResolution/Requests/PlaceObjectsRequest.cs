using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class PlaceObjectsRequest<T> : IStageTaskRequest<Dictionary<DataBinding<T>, Position>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<RectangularZone> DeploymentZone { get; }

        public IReadOnlyList<DataBinding<T>> ModelsToPlace { get; }

        [JsonConstructor]
        public PlaceObjectsRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<RectangularZone> deploymentZone, IReadOnlyList<DataBinding<T>> modelsToPlace)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            DeploymentZone = deploymentZone;
            ModelsToPlace = modelsToPlace;
        }

        public PlaceObjectsRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<RectangularZone> deploymentZone, IReadOnlyList<DataBinding<T>> modelsToPlace)
        {
            TargetPlayerID = targetPlayerID;
            TaskName = taskName;
            DeploymentZone = deploymentZone;
            ModelsToPlace = modelsToPlace;
        }

        public Task<Dictionary<DataBinding<T>, Position>> Resolve(Dictionary<DataBinding<T>, Position> resolution)
        {
            return Task.FromResult(resolution);
        }
    }
}
