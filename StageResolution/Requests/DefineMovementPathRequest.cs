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

        /// <summary>
        /// Whether the moving unit may path through enemy bases (Strafing's fly-over). Derived from the
        /// unit's #042 rules where the request is built, and read by the resolvers so their move-preview
        /// validation agrees with the authoritative stage check. It still may not END on an enemy.
        /// </summary>
        public bool CanMoveThroughEnemies { get; }

        /// <summary>
        /// Whether the moving unit ignores the difficult-terrain move cap (Strider). Derived from the unit's
        /// #042 rules where the request is built, and read by the resolvers so their move-preview validation
        /// agrees with the authoritative stage check (a Strider unit may move full distance across Difficult
        /// terrain without being limited to the difficult-terrain cap).
        /// </summary>
        public bool IgnoresDifficultTerrain { get; }

        /// <summary>
        /// Per-(weapon, enemy unit) effective shooting range where a range-modifier rule changes it
        /// (Increased Shooting Range widens the mover's reach; Ranged Shrouding narrows it for that enemy) —
        /// precomputed engine-side via <see cref="Rules.Dispatch.RangeRuleQueries.EffectiveRange"/> so the
        /// movement "Show targeting" overlay can preview post-move shooting reach in step with the
        /// authoritative <c>ChooseRangedAttackStage</c> check. Only entries that differ from the weapon's base
        /// range are listed; a missing (weapon, enemy) pair means "use <c>weapon.RangeInches</c>". Empty when
        /// no range rule is in play.
        /// </summary>
        public IReadOnlyList<WeaponRangeOverride> WeaponRangeOverrides { get; }

        [JsonConstructor]
        public DefineMovementPathRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitDataBinding, float maxAdvanceDistance, float maxRushDistance, float maxDistanceInches,
            IReadOnlyList<WeaponSightProfile>? weaponSightProfiles = null, bool canMoveThroughEnemies = false,
            bool ignoresDifficultTerrain = false, IReadOnlyList<WeaponRangeOverride>? weaponRangeOverrides = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxAdvanceDistance = maxAdvanceDistance;
            MaxRushDistance = maxRushDistance;
            MaxDistanceInches = maxDistanceInches;
            WeaponSightProfiles = weaponSightProfiles ?? new List<WeaponSightProfile>();
            CanMoveThroughEnemies = canMoveThroughEnemies;
            IgnoresDifficultTerrain = ignoresDifficultTerrain;
            WeaponRangeOverrides = weaponRangeOverrides ?? new List<WeaponRangeOverride>();
        }

        public DefineMovementPathRequest(PlayerID targetPlayerID,  string taskName,
            DataBinding<UnitData> unitDataBinding, float maxAdvanceDistance, float maxRushDistance, float maxDistanceInches,
            IReadOnlyList<WeaponSightProfile>? weaponSightProfiles = null, bool canMoveThroughEnemies = false,
            bool ignoresDifficultTerrain = false, IReadOnlyList<WeaponRangeOverride>? weaponRangeOverrides = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxAdvanceDistance = maxAdvanceDistance;
            MaxRushDistance = maxRushDistance;
            MaxDistanceInches = maxDistanceInches;
            WeaponSightProfiles = weaponSightProfiles ?? new List<WeaponSightProfile>();
            CanMoveThroughEnemies = canMoveThroughEnemies;
            IgnoresDifficultTerrain = ignoresDifficultTerrain;
            WeaponRangeOverrides = weaponRangeOverrides ?? new List<WeaponRangeOverride>();
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

    /// <summary>
    /// The effective shooting range (inches) of one of the mover's ranged weapons against one specific enemy
    /// unit, once range-modifier rules (#102) are folded in — Increased Shooting Range widens it, Ranged
    /// Shrouding narrows it. Keyed by weapon name + the enemy unit's <see cref="UnitID"/> so the movement
    /// targeting overlay can look it up; only emitted when it differs from the weapon's base range.
    /// </summary>
    public record WeaponRangeOverride(string WeaponName, UnitID EnemyUnitId, float EffectiveRangeInches);
}
