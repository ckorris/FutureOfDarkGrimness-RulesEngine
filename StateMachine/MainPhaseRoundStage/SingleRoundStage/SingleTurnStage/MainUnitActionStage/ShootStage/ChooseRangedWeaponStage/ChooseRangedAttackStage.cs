using FDG.Data;
using FDG.StageResolution;
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
        public StageBinding OnNoValidShots;

        public ChooseRangedAttackStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseWeapon = new StageBinding(this);
            BackToChooseAction = new StageBinding(this);
            OnNoValidShots = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedAttackStage)}.");
            }

            //TODO: Handle situations like Deadly, where you have to use a specific weapon first.

            List<ITerrain> terrainSnapshot = context.GameContext.TableState.Terrain.Objects.ToList();

            List<WeaponOption> weaponOptions = BuildWeaponOptions(context.AttackingUnit, context.AvailableWeapons,
                context.GameContext, terrainSnapshot, context.AttackedDefenderRefs);

            if (!HasAnyFireableOption(weaponOptions))
            {
                // No weapon has any selectable target with shooters in range. If the unit has already fired at least
                // once this shoot action, finish the shoot stage; otherwise fall back to the regular cancel path so the
                // player can return to Choose Action without burning their shoot.
                if (context.AlreadyUsedWeapons.Count > 0)
                {
                    GameContext.Log("No remaining weapon has a valid target - ending shoot action.");
                    OnNoValidShots.Activate(context);
                }
                else
                {
                    GameContext.Log("No weapon has a valid target - returning to Choose Action.");
                    BackToChooseAction.Activate(context);
                }
                return;
            }

            ChooseRangedAttackRequest chooseWeaponRequest = new ChooseRangedAttackRequest(context.AttackingUnit.PlayerID(), "Choose Ranged Weapon",
                context.AttackingUnit, weaponOptions);

            CancellableResult<RangedAttackChoice> attackResult = await context.PlayerRequester()
                .RequestDecision<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>(chooseWeaponRequest);

            if (attackResult is Cancelled<RangedAttackChoice>)
            {
                BackToChooseAction.Activate(context);
                return;
            }

            RangedAttackChoice rangedAttackChoice = ((Selected<RangedAttackChoice>)attackResult).Value;

            Weapon chosenWeapon = context.AvailableWeapons.Keys
                .First(option => option.Name == rangedAttackChoice.Weapon.Name);

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            context.SetDefender(rangedAttackChoice.TargetUnit);
            context.RegisterAttackedDefender(rangedAttackChoice.TargetUnit);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            OnChoseWeapon.Activate(context);
        }

        private static bool HasAnyFireableOption(List<WeaponOption> weaponOptions)
        {
            foreach (WeaponOption wo in weaponOptions)
                foreach (WeaponTargetStats ts in wo.WeaponTargetStats)
                    if (ts.UnselectableReason == null && ts.modelsThatCanShoot.Count > 0)
                        return true;
            return false;
        }

        /// <summary>
        /// Returns true if the attacker has at least one ranged weapon that, ignoring any
        /// per-shoot-action target limit, can hit some enemy unit (i.e. some model with the
        /// weapon has both line of sight and range). Used by ChooseActionStage to gray out
        /// Shoot when there is nothing to shoot at.
        /// </summary>
        public static bool HasAnyFireableTarget(DataBinding<UnitData> attackingUnit, IGameContext gameContext)
        {
            UnitData unitValue = attackingUnit.GetValue();
            List<Weapon> rangedWeapons = unitValue.GetRangedWeapons();
            if (rangedWeapons.Count == 0) return false;

            // GetRangedWeapons returns one Weapon instance per model that carries it, so duplicates
            // are normal. BuildWeaponOptions keys by Weapon.Name internally and will throw on collisions,
            // so dedupe by name (same semantics CombatActionContext uses for its AvailableWeapons dict).
            var availableWeapons = new Dictionary<Weapon, int>();
            var seenNames = new HashSet<string>();
            foreach (Weapon w in rangedWeapons)
                if (seenNames.Add(w.Name)) availableWeapons[w] = 1;

            List<ITerrain> terrainSnapshot = gameContext.TableState.Terrain.Objects.ToList();
            List<WeaponOption> options = BuildWeaponOptions(attackingUnit, availableWeapons, gameContext,
                terrainSnapshot, Array.Empty<DataReference>());

            foreach (WeaponOption wo in options)
                foreach (WeaponTargetStats ts in wo.WeaponTargetStats)
                    if (ts.UnselectableReason == null && ts.modelsThatCanShoot.Count > 0)
                        return true;
            return false;
        }

        private static List<WeaponOption> BuildWeaponOptions(DataBinding<UnitData> attackingUnit,
            IReadOnlyDictionary<Weapon, int> availableWeapons, IGameContext gameContext,
            IReadOnlyList<ITerrain> terrain, IReadOnlyCollection<DataReference> attackedDefenderRefs)
        {
            PlayerID playerID = attackingUnit.PlayerID();

            ITeam playerTeam = gameContext.TableState.Teams.Objects
                .First(team => team.IsPlayerOnTeam(playerID));

            Dictionary<string, WeaponOption> nameAndWeaponOptions = new Dictionary<string, WeaponOption>();
            
            foreach(Weapon weapon in availableWeapons.Keys)
            {
                nameAndWeaponOptions.Add(weapon.Name, new WeaponOption(weapon, new List<WeaponTargetStats>()));
            }

            IEnumerable<DataBinding<UnitData>> enemyUnits = gameContext.GameDataStore().GetAllDataBindings<ArmyData>()
                .Where(army => playerTeam.IsPlayerOnTeam(army.GetValue().PlayerID) == false)
                .SelectMany(army => army.GetValue().UnitBindings);

            // If the attacker has already engaged the max number of distinct units this shoot action, any further
            // unit that wasn't already among them is unselectable.
            bool atTargetLimit = attackedDefenderRefs.Count >= GameWideConstants.MAX_TARGETED_UNITS_PER_SHOOT_ACTION;

            //Go through each enemy unit, which will correspond to a WeaponTargetStats.
            foreach (DataBinding<UnitData> enemyUnit in enemyUnits)
            {
                string? unselectableReason = null;
                if (atTargetLimit && !attackedDefenderRefs.Contains(enemyUnit.Reference))
                {
                    unselectableReason = $"Already targeting {GameWideConstants.MAX_TARGETED_UNITS_PER_SHOOT_ACTION} units this shoot action.";
                }

                Dictionary<string, WeaponTargetStats> weaponToStats =
                    BuildAttacksForEnemyUnit(attackingUnit, enemyUnit, availableWeapons.Keys.Select(weapon => weapon.Name),
                        terrain, gameContext, unselectableReason);

                foreach (KeyValuePair<string, WeaponTargetStats> kvp in weaponToStats)
                {
                    nameAndWeaponOptions[kvp.Key].WeaponTargetStats.Add(kvp.Value);
                }
            }

            return nameAndWeaponOptions.Values.ToList();
        }

        private static Dictionary<string, WeaponTargetStats> BuildAttacksForEnemyUnit(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> enemyUnit, IEnumerable<string> weaponNames, IReadOnlyList<ITerrain> terrain,
            IGameContext gameContext, string? unselectableReason = null)
        {
            Dictionary<string, WeaponTargetStats> weaponToStats = new Dictionary<string, WeaponTargetStats>();

            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(
                gameContext.TableState, attackingUnit, enemyUnit);
            IReadOnlyList<ITerrain> allTerrain = terrain.Concat(modelBlockers).ToList();

            bool hasCover = ComputeHasCover(attackingUnit, enemyUnit, allTerrain);

            foreach (string weaponName in weaponNames)
            {
                weaponToStats[weaponName] = new WeaponTargetStats(enemyUnit,
                    new HashSet<DataBinding<ModelData>>(),
                    new HashSet<DataBinding<ModelData>>(),
                    hasCover,
                    unselectableReason);
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

        private static bool CanWeaponShootAtUnit(DataBinding<ModelData> attackingModel,
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