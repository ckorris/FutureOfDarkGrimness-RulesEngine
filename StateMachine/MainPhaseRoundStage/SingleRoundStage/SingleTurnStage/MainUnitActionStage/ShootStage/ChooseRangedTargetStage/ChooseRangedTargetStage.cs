
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
        public StageBinding BackToChooseAction;

        public ChooseRangedTargetStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseTarget = new StageBinding(this);
            BackToChooseAction = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Entered Choose Ranged Target.");

            List<ActionChoice> choices = new List<ActionChoice>();

            PlayerID attackingPlayer = context.AttackingUnit.PlayerID();

            List<ValidRangeTargetOption> validTargets = new List<ValidRangeTargetOption>();
            List<DataBinding<UnitData>> invalidTargets = new List<DataBinding<UnitData>>();

            //Get all armies not on a team with the attacking player.
            ITeam playerTeam = GameContext.TableState.Teams.Objects
                .First(team => team.IsPlayerOnTeam(attackingPlayer));

            foreach (DataBinding<ArmyData> armyBinding in context.GameDataStore().GetAllDataBindings<ArmyData>()
                .Where(army => playerTeam.IsPlayerOnTeam(army.GetValue().PlayerID) == false))
            {
                foreach (DataBinding<UnitData> enemyUnit in armyBinding.GetValue().UnitBindings)
                {
                    bool isValid = IsTargetValid(context.AttackingUnit, enemyUnit,
                        out List<DataBinding<ModelData>> validModels, out List<Weapon> validWeapons);

                    if (isValid)
                    {
                        validTargets.Add(new ValidRangeTargetOption(enemyUnit, validModels, validWeapons));
                    }
                    else
                    {
                        invalidTargets.Add(enemyUnit);
                    }
                }
            }

            ChooseRangedTargetRequest request = new ChooseRangedTargetRequest(attackingPlayer, "Choose Ranged Target", context.AttackingUnit,
                validTargets, invalidTargets);

            DataBinding<UnitData> targetUnit
                = await context.PlayerRequester().RequestDecision<ChooseRangedTargetRequest, DataBinding<UnitData>>(request);

            GameContext.Log($"Chose {targetUnit.Name()} as defender.");
            context.BeginNewAttack(targetUnit);
            OnChoseTarget.Activate(context);
        }

        private static bool IsTargetValid(UnitData attackingUnit, UnitData defendingUnit,
            out List<DataBinding<ModelData>> validAttackingModels, out List<Weapon> validWeapons)
        {
            //Iterate through all models in the attacking unit, and make sure it can see at least one enemy unit.
            //TODO: Not handling cover here.
            validAttackingModels = new List<DataBinding<ModelData>>();
            validWeapons = new List<Weapon>();
            List<Weapon> toRemoveBuffer = new List<Weapon>(); //Reused to remove weapons during iteration.

            foreach (DataBinding<ModelData> attackingModel in attackingUnit.ModelBindings)
            {
                //In GDF as of v3.4, each weapon only needs to see one valid target model to be able to target
                //the whole unit. So once we have that for each weapon, we can stop checking that one.
                //Note: I had an idea to sort the weapon list by range so you can skip all greater than the first,
                //but I don't think that conveys a consistent performance advantage and it makes things more complex.

                //Copy list of weapons, so we can stop checking each once it has a single valid model target.
                List<Weapon> weaponsNotYetHitting = attackingModel.Weapons()
                    .Where(weapon => weapon.IsRanged())
                    .ToList();

                bool canAnyWeaponHit = false;

                foreach (DataBinding<ModelData> targetModel in defendingUnit.ModelBindings)
                {
                    if (DoesModelHaveLineOfSight(attackingModel, targetModel) == false)
                    {
                        continue;
                    }

                    foreach (Weapon weapon in weaponsNotYetHitting)
                    {
                        if (IsTargetWithinRange(attackingModel, targetModel, weapon))
                        {
                            validWeapons.Add(weapon);
                            toRemoveBuffer.Add(weapon);
                            canAnyWeaponHit = true;
                        }
                    }

                    foreach (Weapon toRemove in toRemoveBuffer)
                    {
                        weaponsNotYetHitting.Remove(toRemove);
                    }
                    toRemoveBuffer.Clear();


                    //If all weapons are marked as shootable, there's no reason to keep going.
                    //TODO: May need to do something here for Sniper, or maybe Blast if some people care.
                    if (weaponsNotYetHitting.Count == 0)
                    {
                        continue;
                    }
                }

                //If any can hit, we can mark this model as being able to shoot.
                if (canAnyWeaponHit)
                {
                    validAttackingModels.Add(attackingModel);
                }
            }

            return validWeapons.Count > 0;
        }

        private static bool DoesModelHaveLineOfSight(ModelData attacker, ModelData target)
        {
            //TODO: There's no hard terrain yet as of writing, so always return true.
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