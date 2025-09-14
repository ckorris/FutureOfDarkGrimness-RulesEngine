using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System.Collections.Concurrent;

using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Stages
{

    public class ChooseRangedAttackStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnChoseWeapon;

        public ChooseRangedAttackStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseWeapon = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedAttackStage)}.");
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

            ChooseRangedAttackRequest chooseWeaponRequest = new ChooseRangedAttackRequest(context.AttackingUnit.PlayerID(), "Choose Ranged Weapon",
                context.AttackingUnit, weaponOptions);

            //Some weirdness here because we're not using bindings for weapons as of now.
            //Weapon weaponFromRequest = await context.PlayerRequester().RequestDecision<ChooseRangedWeaponRequest, Weapon>(chooseWeaponRequest);
            RangedAttackChoice rangedAttackChoice = await context.PlayerRequester()
                .RequestDecision<ChooseRangedAttackRequest, RangedAttackChoice>(chooseWeaponRequest);

            Weapon chosenWeapon = validOptions.First(option => option.Item1 == rangedAttackChoice.Weapon.Name).Item2;

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            context.SetDefender(rangedAttackChoice.TargetUnit);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            OnChoseWeapon.Activate(context);
        }

        private List<WeaponOption> GetWeaponOptions(DataBinding<UnitData> attackingUnit,
            IReadOnlyDictionary<Weapon, int> availableWeapons, IGameContext gameContext)
        {
            PlayerID playerID = attackingUnit.PlayerID();

            ITeam playerTeam = GameContext.TableState.Teams.Objects
                .First(team => team.IsPlayerOnTeam(playerID));

            Dictionary<string, WeaponOption> nameAndWeaponOptions = new Dictionary<string, WeaponOption>();
            

            IEnumerable<DataBinding<UnitData>> enemyUnits = gameContext.GameDataStore().GetAllDataBindings<ArmyData>()
                .Where(army => playerTeam.IsPlayerOnTeam(army.GetValue().PlayerID) == false)
                .SelectMany(army => army.GetValue().UnitBindings);

            //Go through each enemy unit, which will correspond to a WeaponTargetStats.
            foreach (DataBinding<UnitData> enemyUnit in enemyUnits)
            {

                Dictionary<string, WeaponTargetStats> weaponToStats =
                    GetAttacksForEnemyUnit(attackingUnit, enemyUnit, nameAndWeaponOptions.Keys);

                

                foreach (KeyValuePair<string, WeaponTargetStats> kvp in weaponToStats)
                {
                    nameAndWeaponOptions[kvp.Key].WeaponTargetStats.Add(kvp.Value);
                }
            }

            return nameAndWeaponOptions.Values.ToList();
        }

        private Dictionary<string, WeaponTargetStats> GetAttacksForEnemyUnit(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> enemyUnit, IEnumerable<string> weaponNames)
        {
            Dictionary<string, WeaponTargetStats> weaponToStats = new Dictionary<string, WeaponTargetStats>();

            foreach (string weaponName in weaponNames)
            {
                weaponToStats[weaponName] = new WeaponTargetStats(enemyUnit,
                    new HashSet<DataBinding<ModelData>>(), 
                    new HashSet<DataBinding<ModelData>>());
            }

            //TODO: Cache line of sight lookups.

            //Go through each of our models that have weapons.
            foreach (DataBinding<ModelData> attackingModel in attackingUnit.ModelBindings())
            {
                //TODO: Cache model weapons, both outside of this to look up, and 
                //within here. Should make a list before this scope of just models with relevant weapons.
                //Also that should have list of relevant weapons.

                Dictionary<DataBinding<ModelData>, bool> lineOfSightCache 
                    = new Dictionary<DataBinding<ModelData>, bool>();

                foreach (Weapon weapon in attackingModel.Weapons()) //For now relying on melee to be out of range.
                {
                    if (weaponToStats.ContainsKey(weapon.Name) == false)
                    {
                        continue;
                    }

                    WeaponTargetStats weaponTargetStats = weaponToStats[weapon.Name];
                    if(CanWeaponShootAtUnit(attackingModel, enemyUnit, weapon,
                        ref lineOfSightCache))
                    {
                        weaponTargetStats.modelsThatCanShoot.Add(attackingModel);
                    }
                    else
                    {
                        weaponTargetStats.modelsWithWeaponThatCannotShoot.Add(attackingModel);
                    }
                }
            }

            return weaponToStats;
        }

        private bool CanWeaponShootAtUnit(DataBinding<ModelData> attackingModel,
            DataBinding<UnitData> enemyUnit, Weapon weapon,
            ref Dictionary<DataBinding<ModelData>, bool> cachedLineOfSights)
        {
            foreach (DataBinding<ModelData> defendingModel in enemyUnit.ModelBindings())
            {

                if (cachedLineOfSights.TryGetValue(defendingModel, out bool hasLineOfSight) == false)
                {
                    hasLineOfSight = DoesModelHaveLineOfSight(attackingModel, defendingModel);
                    cachedLineOfSights[defendingModel] = hasLineOfSight;
                }

                if (hasLineOfSight && IsTargetWithinRange(attackingModel, defendingModel, weapon))
                {
                    return true;
                }
            }

            return false;
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