using FDG.Data;
using Newtonsoft.Json;
using static FDG.StageResolution.Requests.ChooseRangedWeaponRequest;

namespace FDG.StageResolution.Requests
{
    public class ChooseRangedWeaponRequest : IStageTaskRequest<RangedAttackChoice> 
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> AttackingUnit { get; } //Not sure if needed, but can be helpful for the UI.

        public List<WeaponOption> WeaponOptions { get; }

        [JsonConstructor]
        public ChooseRangedWeaponRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> attackingUnit, List<WeaponOption> weaponOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            AttackingUnit = attackingUnit;
            WeaponOptions = weaponOptions;
        }

        public ChooseRangedWeaponRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> attackingUnit, List<WeaponOption> weaponOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            AttackingUnit = attackingUnit;
            WeaponOptions = weaponOptions;
        }

        public Task<RangedAttackChoice> Resolve(RangedAttackChoice resolution)
        {
            return Task.FromResult(resolution);
        }

        public record WeaponOption(Weapon Weapon, List<WeaponTargetStats> WeaponTargetStats);

        /// <summary>
        /// List which models can and cannot shoot at a given unit, of the models in a unit that have a specific weapon.
        /// Lists should not include models that don't have the weapon in question.
        /// </summary>
        /// <param name="TargetUnit">Unit being targeted.</param>
        /// <param name="modelsThatCanShoot">Models with the weapon that can hit (have line of sight and range)</param>
        public record WeaponTargetStats(DataBinding<UnitData> TargetUnit, HashSet<DataBinding<ModelData>> modelsThatCanShoot,
            HashSet<DataBinding<ModelData>> modelsWithWeaponThatCannotShoot);

        /// <summary>
        /// Record suited to choosing your attack, with the attacking unit implied.
        /// </summary>
        /// <param name="Weapon">Weapon used to shoot.</param>
        /// <param name="TargetUnit">Unit to target.</param>
        public record RangedAttackChoice(Weapon Weapon, DataBinding<UnitData> TargetUnit);
    }
}
