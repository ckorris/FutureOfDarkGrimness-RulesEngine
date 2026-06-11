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

        /// <summary>
        /// Per-weapon sighting rules for the moving unit's ranged weapons — whether each ignores cover
        /// (Blast) or intervening terrain (Indirect). The movement resolver doesn't consume this yet, but
        /// it's the info needed to represent "after I move here, can I shoot that" (a cover-/terrain-blocked
        /// target may still be shootable with an ignoring weapon). Empty when the unit has no ranged weapons.
        /// </summary>
        public IReadOnlyList<WeaponSightProfile> WeaponSightProfiles { get; }

        [JsonConstructor]
        public DefineMovementPathRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitDataBinding, float maxAdvanceDistance, float maxRushDistance, float maxDistanceInches,
            IReadOnlyList<WeaponSightProfile>? weaponSightProfiles = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxAdvanceDistance = maxAdvanceDistance;
            MaxRushDistance = maxRushDistance;
            MaxDistanceInches = maxDistanceInches;
            WeaponSightProfiles = weaponSightProfiles ?? new List<WeaponSightProfile>();
        }

        public DefineMovementPathRequest(PlayerID targetPlayerID,  string taskName,
            DataBinding<UnitData> unitDataBinding, float maxAdvanceDistance, float maxRushDistance, float maxDistanceInches,
            IReadOnlyList<WeaponSightProfile>? weaponSightProfiles = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxAdvanceDistance = maxAdvanceDistance;
            MaxRushDistance = maxRushDistance;
            MaxDistanceInches = maxDistanceInches;
            WeaponSightProfiles = weaponSightProfiles ?? new List<WeaponSightProfile>();
        }

        public Task<List<ModelMoveEntry>> Resolve(List<ModelMoveEntry> resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    public record ModelMoveEntry(DataBinding<ModelData> Model, List<Position> Positions);

    /// <summary>
    /// One of the moving unit's ranged weapons and whether it ignores cover (Blast) / line of sight
    /// (Indirect, Takedown). <see cref="CoverIgnoreRule"/> / <see cref="LineOfSightIgnoreRule"/> carry the
    /// alias-aware display name of the responsible rule (null when none), so the movement overlay can
    /// attribute the effect on each fire-line label, e.g. "Huge Gun (Indirect ignores line of sight)".
    /// </summary>
    public record WeaponSightProfile(Weapon Weapon, bool IgnoresCover, bool IgnoresTerrain,
        string? CoverIgnoreRule = null, string? LineOfSightIgnoreRule = null);
}
