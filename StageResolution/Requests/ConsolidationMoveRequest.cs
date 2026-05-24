using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class ConsolidationMoveRequest : IStageTaskRequest<List<ModelMoveEntry>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> UnitDataBinding { get; }

        public float MaxDistanceInches { get; }

        public EConsolidationReason Reason { get; }

        [JsonConstructor]
        public ConsolidationMoveRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitDataBinding, float maxDistanceInches, EConsolidationReason reason)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxDistanceInches = maxDistanceInches;
            Reason = reason;
        }

        public ConsolidationMoveRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> unitDataBinding, float maxDistanceInches, EConsolidationReason reason)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxDistanceInches = maxDistanceInches;
            Reason = reason;
        }

        public Task<List<ModelMoveEntry>> Resolve(List<ModelMoveEntry> resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    public enum EConsolidationReason
    {
        // A unit was wiped out — consolidator may move up to 3" in any direction.
        Wipeout,
        // Neither side wiped — consolidator must disengage back from the defender (up to 1").
        Disengage,
    }
}
