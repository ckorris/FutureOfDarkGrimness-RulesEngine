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

        /// <summary>
        /// When &gt; 0, placed objects must end at least this far (base-edge ignored; center distance)
        /// from every live enemy model. Used by Ambush reserve arrival ("over 9" from enemies");
        /// 0 for normal deployment / Scout (zone containment is the only constraint).
        /// </summary>
        public float MinDistanceFromEnemiesInches { get; }

        [JsonConstructor]
        public PlaceObjectsRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<RectangularZone> deploymentZone, IReadOnlyList<DataBinding<T>> modelsToPlace,
            float minDistanceFromEnemiesInches = 0f)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            DeploymentZone = deploymentZone;
            ModelsToPlace = modelsToPlace;
            MinDistanceFromEnemiesInches = minDistanceFromEnemiesInches;
        }

        public PlaceObjectsRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<RectangularZone> deploymentZone, IReadOnlyList<DataBinding<T>> modelsToPlace,
            float minDistanceFromEnemiesInches = 0f)
        {
            TargetPlayerID = targetPlayerID;
            TaskName = taskName;
            DeploymentZone = deploymentZone;
            ModelsToPlace = modelsToPlace;
            MinDistanceFromEnemiesInches = minDistanceFromEnemiesInches;
        }

        public Task<List<PlacedObjectEntry<T>>> Resolve(List<PlacedObjectEntry<T>> resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    public record PlacedObjectEntry<T>(DataBinding<T> Binding, Position Position);
}
