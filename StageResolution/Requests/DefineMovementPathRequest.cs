using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class DefineMovementPathRequest : IStageTaskRequest<List<ModelMoveEntry>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> UnitDataBinding { get; }

        public float MaxAdvanceDistance { get; }
        public float MaxRushDistance { get; }
        public float MaxDistanceInches { get; }

        [JsonConstructor]
        public DefineMovementPathRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitDataBinding, float maxAdvanceDistance, float maxRushDistance, float maxDistanceInches)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxAdvanceDistance = maxAdvanceDistance;
            MaxRushDistance = maxRushDistance;
            MaxDistanceInches = maxDistanceInches;
        }

        public DefineMovementPathRequest(PlayerID targetPlayerID,  string taskName,
            DataBinding<UnitData> unitDataBinding, float maxAdvanceDistance, float maxRushDistance, float maxDistanceInches)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxAdvanceDistance = maxAdvanceDistance;
            MaxRushDistance = maxRushDistance;
            MaxDistanceInches = maxDistanceInches;
        }

        public Task<List<ModelMoveEntry>> Resolve(List<ModelMoveEntry> resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    public record ModelMoveEntry(DataBinding<ModelData> Model, List<Position> Positions);
}
