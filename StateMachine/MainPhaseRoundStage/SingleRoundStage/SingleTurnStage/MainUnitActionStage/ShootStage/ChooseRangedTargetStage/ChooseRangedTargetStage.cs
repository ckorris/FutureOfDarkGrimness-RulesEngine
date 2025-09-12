/*
using FDG.Data;
using FDG.Utilities;
using FDG.StageResolution.Requests;

using static FDG.StageResolution.Requests.ChooseRangedTargetRequest;

namespace FDG.Stages
{
    public class ChooseRangedTargetStage : StageBase<ICombatActionContext>
    {
        public const string CHOOSE_RANGED_TARGET_TO_FIRE_TRANSITION =
            "ChooseRangedTargetToFire";

        public StageBinding OnChoseTarget;

        public ChooseRangedTargetStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseTarget = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Entered Choose Ranged Target.");

            if(context.ShootingWeaponType == null || context.ShootingWeaponCount == null)
            {
                throw new InvalidOperationException($"Entered {nameof(ChooseRangedTargetStage)} without choosing a ranged weapon.");
            }

            List<ActionChoice> choices = new List<ActionChoice>();

            PlayerID attackingPlayer = context.AttackingUnit.PlayerID();

            List<ValidRangeTargetOption> validTargets = new List<ValidRangeTargetOption>();
            List<DataBinding<UnitData>> invalidTargets = new List<DataBinding<UnitData>>();

            //Currently using weapon name to match against models that have it. Janky, but that's how the real game does it, so...
            string weaponName = context.ShootingWeaponType.Name;
            List<DataBinding<ModelData>> modelsWithSelectedWeapon = context.AttackingUnit.ModelBindings()
                .Where(model => model.Weapons().FirstOrDefault(weapon => weapon.Name == weaponName) != default)
                .ToList();

            //Get all armies not on a team with the attacking player.
            ITeam playerTeam = GameContext.TableState.Teams.Objects
                .First(team => team.IsPlayerOnTeam(attackingPlayer));


            foreach (DataBinding<ArmyData> armyBinding in context.GameDataStore().GetAllDataBindings<ArmyData>()
                .Where(army => playerTeam.IsPlayerOnTeam(army.GetValue().PlayerID) == false))
            {
                foreach (DataBinding<UnitData> enemyUnit in armyBinding.GetValue().UnitBindings)
                {
                    bool isValid = IsTargetValid(context.ShootingWeaponType, modelsWithSelectedWeapon, enemyUnit,
                        out List<DataBinding<ModelData>> validModels);

                    if (isValid)
                    {
                        validTargets.Add(new ValidRangeTargetOption(enemyUnit, validModels));
                    }
                    else
                    {
                        invalidTargets.Add(enemyUnit);
                    }
                }
            }



            ChooseRangedTargetRequest request = new ChooseRangedTargetRequest(attackingPlayer, "Choose Ranged Target", context.AttackingUnit,
                modelsWithSelectedWeapon, context.ShootingWeaponType, context.ShootingWeaponCount.Value,
                validTargets, invalidTargets);

            DataBinding<UnitData> targetUnit
                = await context.PlayerRequester().RequestDecision<ChooseRangedTargetRequest, DataBinding<UnitData>>(request);

            GameContext.Log($"Chose {targetUnit.Name()} as defender.");
            context.SetDefender(targetUnit);
            OnChoseTarget.Activate(context);
        }

        private static bool IsTargetValid(Weapon weaponType, List<DataBinding<ModelData>> modelsWithWeapon, UnitData defendingUnit,
            out List<DataBinding<ModelData>> validAttackingModels)
        {
            //Iterate through all models that have a weapon, and make sure it can see at least one enemy unit.
            //TODO: Not handling cover here.
            validAttackingModels = new List<DataBinding<ModelData>>();

            foreach (DataBinding<ModelData> attackingModel in modelsWithWeapon)
            {

                foreach (DataBinding<ModelData> targetModel in defendingUnit.ModelBindings)
                {
                    if (DoesModelHaveLineOfSight(attackingModel, targetModel) == false) 
                    {
                        continue;
                    }

                    if (IsTargetWithinRange(attackingModel, targetModel, weaponType) == false)
                    {
                        continue;
                    }

                    validAttackingModels.Add(attackingModel);
                    continue;

                }

            }

            return validAttackingModels.Count > 0;
        }

        private static bool DoesModelHaveLineOfSight(ModelData attacker, ModelData target)
        {
            //TODO: There's no hard terrain yet as of writing, so always return true.
            //Also: Incorporate Indirect.
            return true;
        }

        private static bool IsTargetWithinRange(ModelData attacker, ModelData target, Weapon weapon)
        {
            float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(attacker.PositionBinding.GetValue(),
                target.PositionBinding.GetValue(), attacker.BaseRadiusInches, target.BaseRadiusInches);
            return distance <= weapon.RangeInches;
        }
    }
}
*/