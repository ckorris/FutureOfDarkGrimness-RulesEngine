using FDG.Data;
using Newtonsoft.Json;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.StageResolution.Requests
{
    public class ChooseRangedAttackRequest : IStageTaskRequest<CancellableResult<RangedAttackChoice>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> AttackingUnit { get; } //Not sure if needed, but can be helpful for the UI.

        public List<WeaponOption> WeaponOptions { get; }

        [JsonConstructor]
        public ChooseRangedAttackRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> attackingUnit, List<WeaponOption> weaponOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            AttackingUnit = attackingUnit;
            WeaponOptions = weaponOptions;
        }

        public ChooseRangedAttackRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> attackingUnit, List<WeaponOption> weaponOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            AttackingUnit = attackingUnit;
            WeaponOptions = weaponOptions;
        }

        public Task<CancellableResult<RangedAttackChoice>> Resolve(CancellableResult<RangedAttackChoice> resolution)
        {
            return Task.FromResult(resolution);
        }

        /// <param name="IgnoresCover">True if this weapon ignores the target's cover (Blast). Resolvers
        /// should treat a target's <see cref="WeaponTargetStats.HasCover"/> as moot for this weapon when
        /// representing shootability.</param>
        /// <param name="IgnoresTerrain">True if this weapon ignores intervening terrain for line of sight
        /// (Indirect). A seam — not yet derived (Indirect's LoS facet is unimplemented), so false today.</param>
        public record WeaponOption(Weapon Weapon, List<WeaponTargetStats> WeaponTargetStats,
            bool IgnoresCover = false, bool IgnoresTerrain = false);

        /// <summary>
        /// List which models can and cannot shoot at a given unit, of the models in a unit that have a specific weapon.
        /// Lists should not include models that don't have the weapon in question.
        /// </summary>
        /// <param name="TargetUnit">Unit being targeted.</param>
        /// <param name="modelsThatCanShoot">Models with the weapon that can hit (have line of sight and range)</param>
        /// <param name="UnselectableReason">If non-null, the target is unselectable even when it has shooters in range
        /// (e.g. the attacker has already targeted the maximum number of distinct units this shoot action). UIs should
        /// list the target as disabled and surface the reason as a tooltip.</param>
        public record WeaponTargetStats(DataBinding<UnitData> TargetUnit, HashSet<DataBinding<ModelData>> modelsThatCanShoot,
            HashSet<DataBinding<ModelData>> modelsWithWeaponThatCannotShoot, bool HasCover = false,
            string? UnselectableReason = null);

        /// <summary>
        /// Record suited to choosing your attack, with the attacking unit implied.
        /// </summary>
        /// <param name="Weapon">Weapon used to shoot.</param>
        /// <param name="TargetUnit">Unit to target.</param>
        public record RangedAttackChoice(Weapon Weapon, DataBinding<UnitData> TargetUnit);
    }
}
