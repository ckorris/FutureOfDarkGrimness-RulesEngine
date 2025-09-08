using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class ChooseRangedTargetRequest : IStageTaskRequest<DataBinding<UnitData>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> AttackingUnit { get; }

        public List<DataBinding<ModelData>> ModelsWithWeapons { get; } 

        public Weapon Weapon { get; }

        public int WeaponCount { get; }

        public List<ValidRangeTargetOption> ValidRangeTargets { get; }

        public List<DataBinding<UnitData>> InvalidRangeTargets { get; }

        [JsonConstructor]
        public ChooseRangedTargetRequest(PlayerID targetPlayerID, TaskID taskID, string taskName, DataBinding<UnitData> attackingUnit,
            List<DataBinding<ModelData>> modelsWithWeapons, Weapon weapon, int weaponCount,
            List<ValidRangeTargetOption> validRangeTargetOptions, List<DataBinding<UnitData>> invalidRangeTargetOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;

            AttackingUnit = attackingUnit;
            ModelsWithWeapons = modelsWithWeapons;
            Weapon = weapon;
            WeaponCount = weaponCount;

            ValidRangeTargets = validRangeTargetOptions;
            InvalidRangeTargets = invalidRangeTargetOptions;
        }

        public ChooseRangedTargetRequest(PlayerID targetPlayerID,  string taskName, DataBinding<UnitData> attackingUnit,
            List<DataBinding<ModelData>> modelsWithWeapons, Weapon weapon, int weaponCount,
            List<ValidRangeTargetOption> validRangeTargetOptions, List<DataBinding<UnitData>> invalidRangeTargetOptions)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;

            AttackingUnit = attackingUnit;
            ModelsWithWeapons = modelsWithWeapons;
            Weapon = weapon;
            WeaponCount = weaponCount;

            ValidRangeTargets = validRangeTargetOptions;
            InvalidRangeTargets = invalidRangeTargetOptions;
        }

        public Task<DataBinding<UnitData>> Resolve(DataBinding<UnitData> resolution)
        {
            ValidRangeTargetOption? validOption = ValidRangeTargets.FirstOrDefault(option => option.TargetUnit == resolution);

            if(validOption == null)
            {
                throw new ArgumentException($"Tried to resolve {nameof(ChooseRangedTargetRequest)} with a unit that was not " +
                    $"among the list of valid options. Unit name: {resolution.GetValue().Name}");
            }

            return Task.FromResult(resolution);
        }

        public class ValidRangeTargetOption
        {
            public DataBinding<UnitData> TargetUnit;

            public List<DataBinding<ModelData>> ModelsWithValidAttacks;

            public ValidRangeTargetOption(DataBinding<UnitData> targetUnit, List<DataBinding<ModelData>> modelsWithValidAttacks)
            {
                TargetUnit = targetUnit;
                ModelsWithValidAttacks = modelsWithValidAttacks;
            }
        }
    }
}
