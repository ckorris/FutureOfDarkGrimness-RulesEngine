using FDG.Data;
using FDG.Rules.Dispatch;
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

            List<ITerrain> terrainSnapshot = context.GameContext.TableState.Terrain.Objects.ToList();

            List<WeaponOption> weaponOptions = BuildWeaponOptions(context.AttackingUnit, context.AvailableWeapons,
                context.GameContext, terrainSnapshot, context.AttackedDefenderRefs);

            // #028: Deadly (wound-multiplier) weapons must be fired before the unit's other weapons, so a
            // clump removes whole models before normal wounds spread across the unit. Run after option-
            // building so a Deadly weapon with no valid target this action doesn't lock out the rest.
            ApplyDeadlyFirstGating(weaponOptions, context.AttackingUnit, context.GameContext);

            // #032 Limited: a weapon may only be fired once per game. Exclude any Limited weapon whose every
            // living carrier has already fired it (tracked by a per-model token), so it's no longer offered.
            ApplyLimitedSpentGating(weaponOptions, context.AttackingUnit);

            if (!HasAnyFireableOption(weaponOptions))
            {
                // No weapon has any selectable target with shooters in range. If the unit has already fired at least
                // once this shoot action, finish the shoot stage; otherwise fall back to the regular cancel path so the
                // player can return to Choose Action without burning their shoot.
                if (context.AlreadyUsedWeapons.Count > 0)
                {
                    GameContext.Log("No remaining weapon has a valid target - ending shoot action.");
                    await OnNoValidShots.Activate(context);
                }
                else
                {
                    GameContext.Log("No weapon has a valid target - returning to Choose Action.");
                    await BackToChooseAction.Activate(context);
                }
                return;
            }

            ChooseRangedAttackRequest chooseWeaponRequest = new ChooseRangedAttackRequest(context.AttackingUnit.PlayerID(), "Choose Ranged Weapon",
                context.AttackingUnit, weaponOptions);

            CancellableResult<RangedAttackChoice> attackResult = await context.PlayerRequester()
                .RequestDecision<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>(chooseWeaponRequest);

            if (attackResult is Cancelled<RangedAttackChoice>)
            {
                await BackToChooseAction.Activate(context);
                return;
            }

            RangedAttackChoice rangedAttackChoice = ((Selected<RangedAttackChoice>)attackResult).Value;

            Weapon chosenWeapon = context.AvailableWeapons.Keys
                .First(option => option.Name == rangedAttackChoice.Weapon.Name);

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            context.SetDefender(rangedAttackChoice.TargetUnit);
            context.RegisterAttackedDefender(rangedAttackChoice.TargetUnit);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            // #157: a weapon whose attack re-scopes to a single chosen model (Takedown) fires its copies as
            // SEPARATE single-shot attacks so each shot picks its own victim (each sniper chooses a model),
            // instead of one pick funnelling the whole volley. FireStage then loops once per queued shot.
            if (weaponCount > 1 && Rules.Dispatch.SightRuleQueries.TargetsIndividualModels(
                    context.AttackingUnit.GetValue(), chosenWeapon, rangedAttackChoice.TargetUnit.GetValue(),
                    GameContext.RuleEvaluator))
            {
                context.SplitPendingAttackIntoSingleShots();
                GameContext.Log($"{chosenWeapon.Name}: {weaponCount} individually-aimed shots, each picks its own target model.");
            }

            // #032 Limited: choosing the weapon commits it to fire (there's no cancel before FireStage), so mark
            // it spent now — every living carrier records it as fired this game (it's excluded from here on).
            LimitedRules.MarkFired(context.AttackingUnit.GetValue(), chosenWeapon);

            await OnChoseWeapon.Activate(context);
        }

        /// <summary>
        /// #028: while the unit still has an un-fired Deadly (wound-multiplier) weapon with a valid target,
        /// mark every non-Deadly weapon's targets unselectable so the player must resolve Deadly first.
        /// Each fired weapon leaves <see cref="ICombatActionContext.AvailableWeapons"/> once used, so once
        /// the last Deadly weapon is spent this gate no longer fires and the rest become selectable.
        /// </summary>
        private static void ApplyDeadlyFirstGating(List<WeaponOption> weaponOptions,
            DataBinding<UnitData> attackingUnit, IGameContext gameContext)
        {
            UnitData attacker = attackingUnit.GetValue();

            HashSet<string> priorityWeaponNames = weaponOptions
                .Where(option => WoundPriorityQueries.MustResolveFirst(attacker, option.Weapon, gameContext.RuleEvaluator))
                .Select(option => option.Weapon.Name)
                .ToHashSet();

            if (priorityWeaponNames.Count == 0) return;

            // Only gate when a priority weapon actually has a fireable target this action; a Deadly weapon
            // with nothing in range / line of sight must not lock out the unit's other weapons.
            bool anyPriorityFireable = weaponOptions
                .Where(option => priorityWeaponNames.Contains(option.Weapon.Name))
                .SelectMany(option => option.WeaponTargetStats)
                .Any(stats => stats.UnselectableReason == null && stats.modelsThatCanShoot.Count > 0);

            if (!anyPriorityFireable) return;

            foreach (WeaponOption option in weaponOptions)
            {
                if (priorityWeaponNames.Contains(option.Weapon.Name)) continue;

                for (int i = 0; i < option.WeaponTargetStats.Count; i++)
                {
                    WeaponTargetStats stats = option.WeaponTargetStats[i];
                    if (stats.UnselectableReason != null) continue; // keep a more specific reason (target limit, etc.)
                    option.WeaponTargetStats[i] = stats with { UnselectableReason = "Must fire Deadly weapons first." };
                }
            }
        }

        /// <summary>
        /// #032 Limited: a Limited weapon may only be fired once per game. Once every living model carrying it
        /// has fired it (per-model token, via <see cref="LimitedRules.IsSpent"/>), mark its targets unselectable
        /// so the spent weapon is no longer offered. (Same-name weapons fire together, so a unit's copies spend
        /// in one firing — but the per-model check stays correct under casualties and for Limited(X).)
        /// </summary>
        private static void ApplyLimitedSpentGating(List<WeaponOption> weaponOptions,
            DataBinding<UnitData> attackingUnit)
        {
            UnitData attacker = attackingUnit.GetValue();

            foreach (WeaponOption option in weaponOptions)
            {
                if (!LimitedRules.IsSpent(attacker, option.Weapon)) continue;

                for (int i = 0; i < option.WeaponTargetStats.Count; i++)
                {
                    WeaponTargetStats stats = option.WeaponTargetStats[i];
                    if (stats.UnselectableReason != null) continue;
                    option.WeaponTargetStats[i] = stats with { UnselectableReason = "Already fired (Limited)." };
                }
            }
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
            {
                // #032 Limited: a fully-spent Limited weapon can't fire, so it must not keep Shoot available.
                if (LimitedRules.IsSpent(unitValue, wo.Weapon)) continue;
                foreach (WeaponTargetStats ts in wo.WeaponTargetStats)
                    if (ts.UnselectableReason == null && ts.modelsThatCanShoot.Count > 0)
                        return true;
            }
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
            // Per-weapon LoS-ignore (Indirect/Takedown): a weapon that ignores intervening terrain for LoS
            // can hit targets it has no clear line to, so enumeration must not require LoS for it.
            Dictionary<string, bool> weaponIgnoresLineOfSight = new Dictionary<string, bool>();

            foreach(Weapon weapon in availableWeapons.Keys)
            {
                // #042: surface per-weapon whether this weapon ignores cover (Blast) or terrain/LoS
                // (Indirect, Takedown), AND which rule causes it, so the resolver can both know a
                // cover-/LoS-blocked target is still shootable and attribute it to the player ("(Blast
                // ignores cover)"). Derived from the attacker's rules via the shared query (same source the
                // cover and occlusion stages use; the *Source variants are non-logging).
                string? coverIgnoreRule = Rules.Dispatch.SightRuleQueries.CoverIgnoreSource(
                    attackingUnit.GetValue(), weapon, gameContext.RuleEvaluator);
                string? losIgnoreRule = Rules.Dispatch.SightRuleQueries.LineOfSightIgnoreSource(
                    attackingUnit.GetValue(), weapon, gameContext.RuleEvaluator);
                nameAndWeaponOptions.Add(weapon.Name,
                    new WeaponOption(weapon, new List<WeaponTargetStats>(),
                        coverIgnoreRule != null, losIgnoreRule != null, coverIgnoreRule, losIgnoreRule));
                weaponIgnoresLineOfSight.Add(weapon.Name, losIgnoreRule != null);
            }

            IEnumerable<DataBinding<UnitData>> enemyUnits = gameContext.GameDataStore().GetAllDataBindings<ArmyData>()
                .Where(army => playerTeam.IsPlayerOnTeam(army.GetValue().PlayerID) == false)
                .SelectMany(army => army.GetValue().UnitBindings)
                // Reserve units (Ambush) not yet on the table can't be targeted.
                .Where(unit => unit.GetValue().GetIsOnBattlefield());

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
                        terrain, gameContext, weaponIgnoresLineOfSight, unselectableReason);

                foreach (KeyValuePair<string, WeaponTargetStats> kvp in weaponToStats)
                {
                    nameAndWeaponOptions[kvp.Key].WeaponTargetStats.Add(kvp.Value);
                }
            }

            return nameAndWeaponOptions.Values.ToList();
        }

        private static Dictionary<string, WeaponTargetStats> BuildAttacksForEnemyUnit(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> enemyUnit, IEnumerable<string> weaponNames, IReadOnlyList<ITerrain> terrain,
            IGameContext gameContext, IReadOnlyDictionary<string, bool> weaponIgnoresLineOfSight,
            string? unselectableReason = null)
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

            // #102: a weapon's effective range against THIS enemy unit folds the attacker's own range buffs
            // (Increased Shooting Range) and this defender's range debuffs (Ranged Shrouding). The delta is the
            // same for every model of the attacking unit (a unit+weapon+defender property), so compute it once
            // per weapon name and reuse it across the model loop.
            Dictionary<string, float> effectiveRangeByWeapon = new Dictionary<string, float>();

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
                    bool ignoresLoS = weaponIgnoresLineOfSight.TryGetValue(weapon.Name, out bool ig) && ig;

                    if (!effectiveRangeByWeapon.TryGetValue(weapon.Name, out float effectiveRange))
                    {
                        effectiveRange = Rules.Dispatch.RangeRuleQueries.EffectiveRange(
                            attackingUnit.GetValue(), weapon, enemyUnit.GetValue(), gameContext.RuleEvaluator);
                        effectiveRangeByWeapon[weapon.Name] = effectiveRange;
                    }

                    if(CanWeaponShootAtUnit(attackingModel, enemyUnit, effectiveRange,
                        ref lineOfSightCache, allTerrain, ignoresLoS))
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
            DataBinding<UnitData> enemyUnit, float effectiveRangeInches,
            ref Dictionary<DataBinding<ModelData>, bool> cachedLineOfSights,
            IReadOnlyList<ITerrain> terrain, bool ignoresLineOfSight)
        {
            foreach (DataBinding<ModelData> defendingModel in enemyUnit.ModelBindings()
                .Where(model => model.GetIsAlive()))
            {
                // #042 Indirect/Takedown: a weapon that ignores intervening terrain for LoS may fire at a
                // target it has no clear line to, so only range gates it.
                bool hasLineOfSight;
                if (ignoresLineOfSight)
                {
                    hasLineOfSight = true;
                }
                else if (cachedLineOfSights.TryGetValue(defendingModel, out hasLineOfSight) == false)
                {
                    hasLineOfSight = DoesModelHaveLineOfSight(attackingModel.GetValue(), defendingModel.GetValue(), terrain);
                    cachedLineOfSights[defendingModel] = hasLineOfSight;
                }

                if (hasLineOfSight && IsTargetWithinRange(attackingModel.GetValue(), defendingModel.GetValue(), effectiveRangeInches))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DoesModelHaveLineOfSight(ModelData attacker, ModelData target,
            IReadOnlyList<ITerrain> terrain)
        {
            // #042 Indirect/Takedown's LoS-ignore is handled by the caller (CanWeaponShootAtUnit short-
            // circuits before this is reached), since it's a per-weapon property. This is the plain
            // geometric check for weapons that don't ignore LoS.
            return LineOfSightUtilities.HasLineOfSight(
                attacker.PositionBinding.GetValue(),
                target.PositionBinding.GetValue(),
                terrain);
        }

        private static bool IsTargetWithinRange(ModelData attacker, ModelData target, float effectiveRangeInches)
        {
            float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(attacker.PositionBinding.GetValue(),
                target.PositionBinding.GetValue(), attacker.BaseShape, attacker.Facing, target.BaseShape, target.Facing);
            return distance <= effectiveRangeInches;
        }

        // Internal for tests. Dead models must not sway the cover majority (#158): only living defenders
        // can benefit from cover, and only living attackers have sight lines — a squad whose casualties
        // happened to die behind a wall must not grant the survivors standing in the open a cover bonus.
        internal static bool ComputeHasCover(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> defendingUnit, IReadOnlyList<ITerrain> terrain)
        {
            List<DataBinding<ModelData>> attackers = attackingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()).ToList();
            List<DataBinding<ModelData>> defenders = defendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()).ToList();
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