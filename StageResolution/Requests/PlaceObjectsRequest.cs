using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class PlaceObjectsRequest<T> : IStageTaskRequest<List<PlacedObjectEntry<T>>>
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

        public Task<List<PlacedObjectEntry<T>>> Resolve(List<PlacedObjectEntry<T>> resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    public record PlacedObjectEntry<T>(DataBinding<T> Binding, Position Position);
}
