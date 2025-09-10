using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

using static FDG.StageResolution.Requests.ChooseRangedWeaponRequest;

namespace FDG.Stages
{

    public class ChooseRangedWeaponStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnChoseWeapon;

        public ChooseRangedWeaponStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseWeapon = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedWeaponStage)}.");
            }

            //TODO: Handle situations like Deadly, where you have to use a specific weapon first.
            IReadOnlyDictionary<Weapon, int> availableWeapons = new ConcurrentDictionary<Weapon, int>(context.AvailableWeapons);
            IReadOnlyDictionary<Weapon, int> unavailableWeapons = new ConcurrentDictionary<Weapon, int>(context.AlreadyUsedWeapons);

            //TODO: Since we don't store weapons in bindings, we're hackedly using their stats names, which have no
            //protection against identical names.
            List<(string, Weapon)> validOptions = new List<(string, Weapon)>();
            List<StringSelectionRequest.InvalidOption> invalidOptions = new List<StringSelectionRequest.InvalidOption>();

            foreach (KeyValuePair<Weapon, int> kvp in availableWeapons)
            {
                validOptions.Add((kvp.Key.GetWeaponNameAndStats(kvp.Value), kvp.Key));
            }

            foreach (KeyValuePair<Weapon, int> kvp in unavailableWeapons)
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(kvp.Key.GetWeaponNameAndStats(kvp.Value),
                    "The unit has already attacked with this weapon."));
            }


            List<WeaponOption> weaponOptions = GetWeaponOptions(context.AttackingUnit, context.AvailableWeapons, context.GameContext);

            ChooseRangedWeaponRequest chooseWeaponRequest = new ChooseRangedWeaponRequest(context.AttackingUnit.PlayerID(), "Choose Ranged Weapon",
                context.AttackingUnit, weaponOptions);

            //Some weirdness here because we're not using bindings for weapons as of now.
            Weapon weaponFromRequest = await context.PlayerRequester().RequestDecision<ChooseRangedWeaponRequest, Weapon>(chooseWeaponRequest);

            Weapon chosenWeapon = validOptions.First(option => option.Item1 == weaponFromRequest.Name).Item2;

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            /*
            StringSelectionRequest request = new StringSelectionRequest(context.AttackingUnit.PlayerID(),
                "Choose weapon:", validOptions.Select(option => option.Item1).ToList(), invalidOptions);

            string chosenWeaponStatsName = await GameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);
            
            Weapon chosenWeapon = validOptions.First(option => option.Item1 == chosenWeaponStatsName).Item2;

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");
            */

            OnChoseWeapon.Activate(context);
        }



        /*
         *The below was taken from ChooseRangedTargetStage when it came before weapons. Maybe of use?
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
        */

        private List<WeaponOption> GetWeaponOptions(DataBinding<UnitData> attackingUnit,
            IReadOnlyDictionary<Weapon, int> availableWeapons, IGameContext gameContext)
        {
            PlayerID playerID = attackingUnit.PlayerID();

            ITeam playerTeam = GameContext.TableState.Teams.Objects
                .First(team => team.IsPlayerOnTeam(playerID));

            Dictionary<string, WeaponOption> nameAndWeaponOptions = new Dictionary<string, WeaponOption>();

            //Reuse the below.
            Dictionary<string, WeaponTargetStats> weaponToStatsCache = new Dictionary<string, WeaponTargetStats>();

            foreach (Weapon weapon in availableWeapons.Keys)
            {
                WeaponOption option = new WeaponOption(weapon, new List<WeaponTargetStats>());
                nameAndWeaponOptions.Add(weapon.Name, option);

                weaponToStatsCache.Add(weapon.Name, null);
            }

            IEnumerable<DataBinding<UnitData>> enemyUnits = gameContext.GameDataStore().GetAllDataBindings<ArmyData>()
                .Where(army => playerTeam.IsPlayerOnTeam(army.GetValue().PlayerID) == false)
                .SelectMany(army => army.GetValue().UnitBindings);

            //Go through each enemy unit, which will correspond to a WeaponTargetStats.
            foreach (DataBinding<UnitData> enemyUnit in enemyUnits)
            {
                //Okay I hate how layered this is.
                foreach (KeyValuePair<string, WeaponTargetStats> stats in weaponToStatsCache)
                {
                    weaponToStatsCache[stats.Key] = new WeaponTargetStats(enemyUnit, new List<DataBinding<ModelData>>(), new List<DataBinding<ModelData>>());
                }

                //Go through each of our models that have weapons.
                foreach (DataBinding<ModelData> attackingModel in attackingUnit.ModelBindings())
                {
                    //TODO: Cache model weapons, both outside of this to look up, and 
                    //within here. Should make a list before this scope of just models with relevant weapons.
                    //Also that should have list of relevant weapons.

                    foreach (Weapon weapon in attackingModel.Weapons()) //For now relying on melee to be out of range.
                    {
                        if (nameAndWeaponOptions.ContainsKey(weapon.Name) == false)
                        {
                            continue;
                        }

                        WeaponTargetStats targetStats = weaponToStatsCache[weapon.Name];
                        bool foundOne = false;

                        foreach (DataBinding<ModelData> defendingModel in enemyUnit.ModelBindings())
                        {
                            bool hasLineOfSight = DoesModelHaveLineOfSight(attackingModel, defendingModel);

                            if (hasLineOfSight && IsTargetWithinRange(attackingModel, defendingModel, weapon))
                            {
                                //Gaaaawd this is so much branching.
                                targetStats.modelsThatCanShoot.Add(attackingModel);
                                foundOne = true;
                                break;
                            }
                        }

                        if(foundOne == false)
                        {
                            targetStats.modelsWithWeaponThatCannotShoot.Add(attackingModel);
                        }
                    }
                }

                foreach (KeyValuePair<string, WeaponTargetStats> kvp in weaponToStatsCache)
                {
                    nameAndWeaponOptions[kvp.Key].WeaponTargetStats.Add(kvp.Value);
                }
            }

            return nameAndWeaponOptions.Values.ToList();
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