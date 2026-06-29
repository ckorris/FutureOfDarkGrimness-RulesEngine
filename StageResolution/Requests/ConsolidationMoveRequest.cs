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

        /// <summary>
        /// Whether the consolidating unit may path through enemy bases (Strafing's fly-over). Derived from
        /// the unit's #042 rules where the request is built, and read by the resolvers so their move-preview
        /// validation agrees with the authoritative <see cref="Stages.ConsolidateStage"/> check.
        /// </summary>
        public bool CanMoveThroughEnemies { get; }

        /// <summary>
        /// Whether the consolidating unit ignores the difficult-terrain move cap (Strider). Derived from the
        /// unit's #042 rules where the request is built, and read by the resolvers so their move-preview
        /// validation agrees with the authoritative <see cref="Stages.ConsolidateStage"/> check.
        /// </summary>
        public bool IgnoresDifficultTerrain { get; }

        [JsonConstructor]
        public ConsolidationMoveRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitDataBinding, float maxDistanceInches, EConsolidationReason reason,
            bool canMoveThroughEnemies = false, bool ignoresDifficultTerrain = false)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxDistanceInches = maxDistanceInches;
            Reason = reason;
            CanMoveThroughEnemies = canMoveThroughEnemies;
            IgnoresDifficultTerrain = ignoresDifficultTerrain;
        }

        public ConsolidationMoveRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> unitDataBinding, float maxDistanceInches, EConsolidationReason reason,
            bool canMoveThroughEnemies = false, bool ignoresDifficultTerrain = false)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxDistanceInches = maxDistanceInches;
            Reason = reason;
            CanMoveThroughEnemies = canMoveThroughEnemies;
            IgnoresDifficultTerrain = ignoresDifficultTerrain;
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
