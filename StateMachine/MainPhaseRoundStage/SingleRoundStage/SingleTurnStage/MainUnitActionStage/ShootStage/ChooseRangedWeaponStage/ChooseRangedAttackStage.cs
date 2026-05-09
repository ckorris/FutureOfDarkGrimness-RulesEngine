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
        public StageBinding BackToChooseAction;

        public ChooseRangedAttackStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseWeapon = new StageBinding(this);
            BackToChooseAction = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedAttackStage)}.");
            }

            //TODO: Handle situations like Deadly, where you have to use a specific weapon first.

            List<ITerrain> terrainSnapshot = context.GameContext.TableState.Terrain.Objects.ToList();

            List<WeaponOption> weaponOptions = GetWeaponOptions(context.AttackingUnit, context.AvailableWeapons,
                context.GameContext, terrainSnapshot);

            ChooseRangedAttackRequest chooseWeaponRequest = new ChooseRangedAttackRequest(context.AttackingUnit.PlayerID(), "Choose Ranged Weapon",
                context.AttackingUnit, weaponOptions);

            //Some weirdness here because we're not using bindings for weapons as of now.
            //Weapon weaponFromRequest = await context.PlayerRequester().RequestDecision<ChooseRangedWeaponRequest, Weapon>(chooseWeaponRequest);
            RangedAttackChoice? rangedAttackChoice = await context.PlayerRequester()
                .RequestDecision<ChooseRangedAttackRequest, RangedAttackChoice>(chooseWeaponRequest);

            if (rangedAttackChoice == null)
            {
                BackToChooseAction.Activate(context);
                return;
            }

            //Weapon chosenWeapon = validOptions.First(option => option.Item2.Name == rangedAttackChoice.Weapon.Name).Item2;
            Weapon chosenWeapon = context.AvailableWeapons.Keys
                .First(option => option.Name == rangedAttackChoice.Weapon.Name);

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            context.SetDefender(rangedAttackChoice.TargetUnit);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            OnChoseWeapon.Activate(context);
        }

        private List<WeaponOption> GetWeaponOptions(DataBinding<UnitData> attackingUnit,
            IReadOnlyDictionary<Weapon, int> availableWeapons, IGameContext gameContext,
            IReadOnlyList<ITerrain> terrain)
        {
            PlayerID playerID = attackingUnit.PlayerID();

            ITeam playerTeam = GameContext.TableState.Teams.Objects
                .First(team => team.IsPlayerOnTeam(playerID));

            Dictionary<string, WeaponOption> nameAndWeaponOptions = new Dictionary<string, WeaponOption>();
            
            foreach(Weapon weapon in availableWeapons.Keys)
            {
                nameAndWeaponOptions.Add(weapon.Name, new WeaponOption(weapon, new List<WeaponTargetStats>()));
            }

            IEnumerable<DataBinding<UnitData>> enemyUnits = gameContext.GameDataStore().GetAllDataBindings<ArmyData>()
                .Where(army => playerTeam.IsPlayerOnTeam(army.GetValue().PlayerID) == false)
                .SelectMany(army => army.GetValue().UnitBindings);

            //Go through each enemy unit, which will correspond to a WeaponTargetStats.
            foreach (DataBinding<UnitData> enemyUnit in enemyUnits)
            {
                Dictionary<string, WeaponTargetStats> weaponToStats =
                    GetAttacksForEnemyUnit(attackingUnit, enemyUnit, availableWeapons.Keys.Select(weapon => weapon.Name), terrain);

                foreach (KeyValuePair<string, WeaponTargetStats> kvp in weaponToStats)
                {
                    nameAndWeaponOptions[kvp.Key].WeaponTargetStats.Add(kvp.Value);
                }
            }

            return nameAndWeaponOptions.Values.ToList();
        }

        private Dictionary<string, WeaponTargetStats> GetAttacksForEnemyUnit(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> enemyUnit, IEnumerable<string> weaponNames, IReadOnlyList<ITerrain> terrain)
        {
            Dictionary<string, WeaponTargetStats> weaponToStats = new Dictionary<string, WeaponTargetStats>();

            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(
                GameContext.TableState, attackingUnit, enemyUnit);
            IReadOnlyList<ITerrain> allTerrain = terrain.Concat(modelBlockers).ToList();

            bool hasCover = ComputeHasCover(attackingUnit, enemyUnit, allTerrain);

            foreach (string weaponName in weaponNames)
            {
                weaponToStats[weaponName] = new WeaponTargetStats(enemyUnit,
                    new HashSet<DataBinding<ModelData>>(),
                    new HashSet<DataBinding<ModelData>>(),
                    hasCover);
            }

            //TODO: Cache line of sight lookups.

            //Go through each of our models that have weapons.
            foreach (DataBinding<ModelData> attackingModel in attackingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()))
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
                        ref lineOfSightCache, allTerrain))
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
            ref Dictionary<DataBinding<ModelData>, bool> cachedLineOfSights,
            IReadOnlyList<ITerrain> terrain)
        {
            foreach (DataBinding<ModelData> defendingModel in enemyUnit.ModelBindings()
                .Where(model => model.GetIsAlive()))
            {

                if (cachedLineOfSights.TryGetValue(defendingModel, out bool hasLineOfSight) == false)
                {
                    hasLineOfSight = DoesModelHaveLineOfSight(attackingModel.GetValue(), defendingModel.GetValue(), terrain);
                    cachedLineOfSights[defendingModel] = hasLineOfSight;
                }

                if (hasLineOfSight && IsTargetWithinRange(attackingModel, defendingModel, weapon))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DoesModelHaveLineOfSight(ModelData attacker, ModelData target,
            IReadOnlyList<ITerrain> terrain)
        {
            //TODO (TERRAIN_FOLLOWUPS.md): Incorporate Indirect at the call site, before the
            //per-defender LoS cache is consulted, since Indirect is weapon-scoped.
            return LineOfSightUtilities.HasLineOfSight(
                attacker.PositionBinding.GetValue(),
                target.PositionBinding.GetValue(),
                terrain);
        }

        private static bool IsTargetWithinRange(ModelData attacker, ModelData target, Weapon weapon)
        {
            float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(attacker.PositionBinding.GetValue(),
                target.PositionBinding.GetValue(), attacker.BaseRadiusInches, target.BaseRadiusInches);
            return distance <= weapon.RangeInches;
        }

        private static bool ComputeHasCover(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> defendingUnit, IReadOnlyList<ITerrain> terrain)
        {
            List<DataBinding<ModelData>> attackers = attackingUnit.ModelBindings().ToList();
            List<DataBinding<ModelData>> defenders = defendingUnit.ModelBindings().ToList();
            if (defenders.Count == 0) return false;

            int modelsInCover = 0;
            foreach (DataBinding<ModelData> defender in defenders)
            {
                Position defPos = defender.GetValue().PositionBinding.GetValue();
                foreach (DataBinding<ModelData> attacker in attackers)
                {
                    ESightLineEffect effect = LineOfSightUtilities.EvaluateSightLine(
                        attacker.GetValue().PositionBinding.GetValue(), defPos, terrain);
                    if (effect == ESightLineEffect.Cover)
                    {
                        modelsInCover++;
                        break;
                    }
                }
            }

            return modelsInCover * 2 > defenders.Count;
        }
    }
}