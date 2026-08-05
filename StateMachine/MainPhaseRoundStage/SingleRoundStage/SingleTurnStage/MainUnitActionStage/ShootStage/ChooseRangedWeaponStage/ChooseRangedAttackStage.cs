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

        /// <summary>
        /// #319: the weapon choice repeats within a single entry whenever the player HOLDS FIRE with a weapon
        /// - that weapon leaves the action's pool and the remaining ones are offered again, without firing
        /// anything and without leaving the stage. Everything else (a choice, a Back, a Done) exits the loop
        /// on its first pass, exactly as before.
        /// </summary>
        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedAttackStage)}.");
            }

            while (true)
            {
                bool holdingFire = await OfferWeapons(context);
                if (!holdingFire) return;
            }
        }

        /// <summary>
        /// One pass of the weapon offer. Returns true when the player held fire and weapons remain, i.e.
        /// <see cref="Enter"/> should offer again; false when this stage has routed onward (fired, backed
        /// out, ended the shoot, or nothing fireable is left).
        /// </summary>
        private async Task<bool> OfferWeapons(ICombatActionContext context)
        {
            List<ITerrain> terrainSnapshot = context.GameContext.TableState.Terrain.Objects.ToList();

            List<WeaponOption> weaponOptions = BuildWeaponOptions(context.AttackingUnit, context.AvailableWeapons,
                context.GameContext, terrainSnapshot, context.AttackedDefenderRefs);

            // #325: price every fireable row BEFORE the request goes out - the resolvers cannot (weapon
            // rules never cross the wire). Here and not in BuildWeaponOptions, because HasAnyFireableTarget
            // reuses the builder every time the action menu opens and only needs the boolean.
            ShootingForecast.Attach(weaponOptions, context.AttackingUnit, context.GameContext,
                context.AttackerMoved);

            ApplyTargetGating(weaponOptions, context.AttackingUnit, context.GameContext,
                context.MarkedTargetsOnly);

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
                return false;
            }

            // #308: the stage owns both "may the player back out?" and "what did the last weapon shoot?".
            // Backing out is legal exactly while nothing has fired this shoot action - the same
            // AlreadyUsedWeapons test the no-valid-shots branch above uses, so the Back button and the
            // fall-back path can never disagree. (The GUI used to decide this itself with a fire counter
            // it only reset when the ATTACKER changed, which permanently hid Back for any unit that had
            // ever shot.) PreviousTarget rides along so the next weapon starts aimed where the last one
            // fired, when that is still legal.
            // #319: exactly one of the two exits is offered, and both come back as Cancelled - "Back"
            // (nothing fired yet) rewinds to Choose Action, "Done shooting" (something fired) ends the
            // action here. Offering the second is what lets a player decline a weapon they would rather
            // not spend, a once-per-game Limited one above all.
            ChooseRangedAttackRequest chooseWeaponRequest = new ChooseRangedAttackRequest(context.AttackingUnit.PlayerID(), "Choosing a Ranged Weapon",
                context.AttackingUnit, weaponOptions,
                allowCancel: context.AlreadyUsedWeapons.Count == 0,
                previousTarget: context.DefendingUnit,
                allowStopShooting: context.AlreadyUsedWeapons.Count > 0);

            CancellableResult<RangedAttackChoice> attackResult = await context.PlayerRequester()
                .RequestDecision<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>(chooseWeaponRequest);

            if (attackResult is Cancelled<RangedAttackChoice>)
            {
                // #308: a cancel after the first weapon has fired can't go back to Choose Action - the
                // shoot has already happened and re-offering the action menu would hand the unit a second
                // one. #319 turned that fallback into an offered choice ("Done shooting"), but the routing
                // is unchanged and still decided here, so a resolver can never get it wrong.
                if (context.AlreadyUsedWeapons.Count > 0)
                {
                    GameContext.Log("Done shooting - ending the shoot action with weapons unfired.");
                    await OnNoValidShots.Activate(context);
                    return false;
                }

                await BackToChooseAction.Activate(context);
                return false;
            }

            RangedAttackChoice rangedAttackChoice = ((Selected<RangedAttackChoice>)attackResult).Value;

            Weapon chosenWeapon = ResolveChosenWeapon(context, rangedAttackChoice.Weapon);

            // #319: hold fire - the weapon drops out of this action unfired. Nothing is spent (a Limited
            // weapon keeps its shot) and, since the gates above read the AVAILABLE pool, a declined
            // resolve-first weapon stops locking out the unit's ordinary ones on the next pass.
            if (rangedAttackChoice.IsHoldFire)
            {
                context.DeclineWeapon(chosenWeapon);
                GameContext.Log($"Holding fire with {chosenWeapon.Name} - not used this shoot action.");

                if (context.AvailableWeapons.Count > 0) return true;

                if (context.AlreadyUsedWeapons.Count > 0)
                {
                    GameContext.Log("No weapons left to fire - ending shoot action.");
                    await OnNoValidShots.Activate(context);
                }
                else
                {
                    GameContext.Log("Held fire with every weapon - returning to Choose Action.");
                    await BackToChooseAction.Activate(context);
                }
                return false;
            }

            DataBinding<UnitData> targetUnit = rangedAttackChoice.TargetUnit!;

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            context.SetDefender(targetUnit);
            context.RegisterAttackedDefender(targetUnit);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            // #340: a weapon whose attack re-scopes to a single chosen model (Takedown / Sniper) is aimed
            // ONE COPY AT A TIME - each rifle picks its own target UNIT as well as its own victim model.
            // Only one copy is committed here; the rest go straight back into the action's pool, so this
            // stage offers the weapon again and the player (or AI) chooses afresh, target limit and all.
            // #157 split the volley into per-shot MODEL picks but left every shot locked onto the unit
            // chosen for the first one, which is the bug this replaces.
            bool aimsOneCopyAtATime = Rules.Dispatch.SightRuleQueries.TargetsIndividualModels(
                context.AttackingUnit.GetValue(), chosenWeapon, targetUnit.GetValue(),
                GameContext.RuleEvaluator);

            if (aimsOneCopyAtATime)
            {
                context.AimPendingAttackOneCopyAtATime(out int copiesHeldBack);
                if (copiesHeldBack > 0)
                {
                    GameContext.Log($"{chosenWeapon.Name}: firing 1 of {weaponCount} - the other " +
                        $"{copiesHeldBack} {(copiesHeldBack == 1 ? "aims" : "aim")} separately and " +
                        $"{(copiesHeldBack == 1 ? "is" : "are")} offered again.");
                }
            }
            else
            {
                // #276: only the copies carried by models that can actually shoot the chosen target fire —
                // GDF checks range and line of sight per model, and the option builder above already computed
                // exactly that (modelsThatCanShoot, the same truth the targeting UI shows). The pooled
                // weapon count includes every living carrier, so cap it here before the roll. Eligible == 0
                // can't normally happen (such targets are unselectable); if it does, leave the count alone
                // rather than firing a zero-dice attack.
                // Skipped for a one-at-a-time weapon: exactly one copy fires, its carrier is picked from the
                // eligible ones by the attack beat, and trimming the stack would strip the copies that are
                // going back into the pool to be aimed elsewhere.
                int eligibleCount = CountEligibleCopies(weaponOptions, chosenWeapon, targetUnit);
                if (eligibleCount > 0 && eligibleCount < weaponCount)
                {
                    context.TrimPendingAttack(eligibleCount);
                    GameContext.Log($"{eligibleCount} of {weaponCount} {chosenWeapon.Name} copies have " +
                        "line of sight and range - the rest hold fire.");
                    weaponCount = eligibleCount;
                }
            }

            // #032 Limited: choosing the weapon commits it to fire (there's no cancel before FireStage), so mark
            // it spent now — every living carrier records it as fired this game (it's excluded from here on).
            // #340: a one-at-a-time weapon spends ONE carrier per firing instead, or the first rifle would
            // burn the once-per-game shot of the ones still waiting to be aimed.
            // #319: and SAY so. Spending a once-per-game weapon is the single most consequential thing this
            // stage can do, and until now it happened silently; the player could decline it (Hold fire) right
            // up to this point, so the log has to record which way it went.
            string? limitedRule = LimitedRules.LimitedRuleName(chosenWeapon);
            if (aimsOneCopyAtATime)
            {
                LimitedRules.MarkOneCarrierFired(context.AttackingUnit.GetValue(), chosenWeapon);
            }
            else
            {
                LimitedRules.MarkFired(context.AttackingUnit.GetValue(), chosenWeapon);
            }
            if (limitedRule != null)
            {
                GameContext.Log(aimsOneCopyAtATime
                    ? $"{chosenWeapon.Name} is {limitedRule} - this copy is spent for the rest of the game."
                    : $"{chosenWeapon.Name} is {limitedRule} - spent for the rest of the game.");
            }

            await OnChoseWeapon.Activate(context);
            return false;
        }

        /// <summary>
        /// #306: maps the resolver's reply back onto the pool entry it names. The reply may be a
        /// DESERIALIZED weapon (a remote player's answer travels as JSON), so its rules arrive only in
        /// their persisted form — <see cref="Weapon.RehydrateRules"/> restores them, and is a no-op for a
        /// local resolver, which hands back the pool's own instance. Matching on the PROFILE rather than
        /// the bare name is what lets a unit carry two same-named weapons with different rules and still
        /// fire the one the player picked.
        /// </summary>
        private Weapon ResolveChosenWeapon(ICombatActionContext context, Weapon chosenFromResolver)
        {
            chosenFromResolver.RehydrateRules();
            string chosenKey = WeaponProfileKey.For(chosenFromResolver);

            Weapon? byProfile = context.AvailableWeapons.Keys
                .FirstOrDefault(available => WeaponProfileKey.For(available) == chosenKey);
            if (byProfile != null) return byProfile;

            // A reply whose profile matches nothing available is a bug somewhere upstream, but faulting
            // the state machine mid-activation is the one outcome worth avoiding here (it is exactly what
            // #306 set out to remove). Degrade to the pre-#306 name match and say so.
            GameContext.Log($"Chosen weapon '{chosenFromResolver.Name}' did not match any available " +
                "weapon profile - falling back to the first copy carrying that name.");
            return context.AvailableWeapons.Keys.First(available => available.Name == chosenFromResolver.Name);
        }

        /// <summary>
        /// The post-build gating pipeline, shared by <see cref="Enter"/> and
        /// <see cref="HasAnyFireableTarget"/> so the Shoot ACTION gate and the shoot STAGE can never
        /// disagree about whether anything is fireable (#200 - they did, and a deterministic AI
        /// livelocked on the Choose Action / Shoot bounce).
        /// ORDER MATTERS (#200's root cause): Limited-spent gating must run BEFORE resolve-first
        /// gating - a spent Limited+Deadly weapon can never fire again, so it must not count as a
        /// priority weapon that locks out the unit's other weapons ("fire the empty rocket first").
        /// </summary>
        private static void ApplyTargetGating(List<WeaponOption> weaponOptions,
            DataBinding<UnitData> attackingUnit, IGameContext gameContext, bool markedTargetsOnly)
        {
            // #032 Limited: a weapon may only be fired once per game. Exclude any Limited weapon whose
            // every living carrier has already fired it (per-model token), so it's no longer offered.
            ApplyLimitedSpentGating(weaponOptions, attackingUnit);

            // #028/#314: resolve-first weapons must be fired before the unit's other weapons - Deadly
            // (a clump removes whole models before normal wounds spread) and Takedown ("Takedown attacks
            // must be resolved before other weapons"). Runs after option-building AND after Limited
            // gating: a priority weapon with no valid target - or one already spent - must not lock out
            // the rest.
            ApplyResolveFirstGating(weaponOptions, attackingUnit, gameContext);

            // #197 P20: when the only thing permitting this shot is a Quick Shot mark on some enemy, the
            // permission is target-bound - every unmarked target becomes unselectable.
            ApplyQuickShotMarkGating(weaponOptions, gameContext, markedTargetsOnly);

            // #197 Instinctive: a compelled unit's shot must go to the closest valid target. Runs LAST so
            // "valid" means what survived every gate above - if the closest enemy is spent-Limited-only,
            // Deadly-locked or unmarked, the compulsion falls on the closest target the unit may actually
            // shoot, never on one it may not.
            ApplyClosestTargetGating(weaponOptions, attackingUnit, gameContext);
        }

        /// <summary>
        /// #197 Instinctive: "it must immediately attack the closest valid target". When the attacker is
        /// under the live compulsion (stamped at activation), every fireable target that is not (within a
        /// float tolerance) the closest fireable one becomes unselectable, so the request the resolver
        /// receives - human or AI - simply has no non-compliant option (the P20 Quick Shot pattern).
        /// Unit-level closest: one enemy is "the" target for the whole shoot action; a weapon that cannot
        /// reach it holds fire. Ties within tolerance all stay selectable - the rule names one closest,
        /// and when geometry offers two the pick is the player's.
        /// </summary>
        private static void ApplyClosestTargetGating(List<WeaponOption> weaponOptions,
            DataBinding<UnitData> attackingUnit, IGameContext gameContext)
        {
            string? source = Rules.Dispatch.CapabilityRuleQueries.IsCompelledToAttackClosest(
                attackingUnit.GetValue(), gameContext.RuleEvaluator);
            if (source == null) return;

            // One distance per enemy unit (living-model minimum, the standard measure), shared across the
            // per-weapon rows. Only genuinely fireable rows compete for "closest".
            Dictionary<DataReference, float> distanceByTarget = new Dictionary<DataReference, float>();
            float closest = float.MaxValue;
            foreach (WeaponOption option in weaponOptions)
            {
                foreach (WeaponTargetStats stats in option.WeaponTargetStats)
                {
                    if (stats.UnselectableReason != null || stats.modelsThatCanShoot.Count == 0) continue;
                    if (!distanceByTarget.TryGetValue(stats.TargetUnit.Reference, out float distance))
                    {
                        distance = UnitCompareUtilities.MinDistanceBetweenUnits(attackingUnit.GetValue(),
                            stats.TargetUnit.GetValue(), out _, out _, includeVertical: false);
                        distanceByTarget[stats.TargetUnit.Reference] = distance;
                    }
                    if (distance < closest) closest = distance;
                }
            }

            if (closest == float.MaxValue) return; // nothing fireable - nothing to narrow

            foreach (WeaponOption option in weaponOptions)
            {
                for (int i = 0; i < option.WeaponTargetStats.Count; i++)
                {
                    WeaponTargetStats stats = option.WeaponTargetStats[i];
                    // Rows that are already unselectable keep their more specific reason, and rows with no
                    // shooters are unfireable anyway - overwriting either would tell the player the wrong why.
                    if (stats.UnselectableReason != null || stats.modelsThatCanShoot.Count == 0) continue;
                    if (distanceByTarget[stats.TargetUnit.Reference] > closest + 0.001f)
                    {
                        option.WeaponTargetStats[i] = stats with
                        {
                            UnselectableReason = $"{source} - must attack the closest target.",
                        };
                    }
                }
            }
        }

        /// <summary>
        /// #197 P20 Quick Shot Mark: "pick one enemy unit, which friendly units get Quick Shot AGAINST
        /// once". A unit that Rushed and has no Quick Shot of its own may still shoot - but only the marked
        /// enemy, otherwise the mark would license a free shot at anything and never even be claimed
        /// (the mark is only spent when the attack lands on the unit carrying it).
        /// </summary>
        private static void ApplyQuickShotMarkGating(List<WeaponOption> weaponOptions,
            IGameContext gameContext, bool markedTargetsOnly)
        {
            if (!markedTargetsOnly) return;

            IRuleResolver? resolver = gameContext.RuleEvaluator.RuleResolver;

            foreach (WeaponOption option in weaponOptions)
            {
                for (int i = 0; i < option.WeaponTargetStats.Count; i++)
                {
                    WeaponTargetStats stats = option.WeaponTargetStats[i];
                    if (stats.UnselectableReason != null) continue;
                    if (ShootAfterRushRules.MarkGrantsShootAfterRush(stats.TargetUnit.GetValue(), resolver))
                    {
                        continue;
                    }

                    option.WeaponTargetStats[i] = stats with
                    {
                        UnselectableReason = "Rushed - only a Quick Shot marked target may be shot.",
                    };
                }
            }
        }

        /// <summary>
        /// #028/#314: while the unit still has an un-fired resolve-first weapon (Deadly's wound multiplier
        /// or Takedown's individual-target re-scope) with a valid target, mark every ordinary weapon's
        /// targets unselectable so the player must resolve the priority weapons first.
        /// Each fired weapon leaves <see cref="ICombatActionContext.AvailableWeapons"/> once used, so once
        /// the last priority weapon is spent this gate no longer fires and the rest become selectable.
        /// Deadly and Takedown share ONE priority class - neither rule claims precedence over the other,
        /// so a unit carrying both fires both before its ordinary weapons in whichever order it likes.
        /// </summary>
        private static void ApplyResolveFirstGating(List<WeaponOption> weaponOptions,
            DataBinding<UnitData> attackingUnit, IGameContext gameContext)
        {
            UnitData attacker = attackingUnit.GetValue();

            // #306: profile-keyed, not name-keyed - a partial upgrade can leave a unit with a Deadly
            // "Sword" and a plain "Sword", and only the Deadly one must gate the rest.
            // #314: the source name rides along so the reason text names the rule actually gating
            // ("Deadly(3)" or "Takedown"), alias-aware. Distinct names are joined, since a unit may carry
            // one of each and both must fire before the ordinary weapons.
            HashSet<string> priorityWeaponKeys = new HashSet<string>(StringComparer.Ordinal);
            SortedSet<string> prioritySources = new SortedSet<string>(StringComparer.Ordinal);
            foreach (WeaponOption option in weaponOptions)
            {
                string? source = WoundPriorityQueries.ShootingResolveFirstSource(
                    attacker, option.Weapon, gameContext.RuleEvaluator);
                if (source == null) continue;
                priorityWeaponKeys.Add(WeaponProfileKey.For(option.Weapon));
                prioritySources.Add(source);
            }

            if (priorityWeaponKeys.Count == 0) return;

            // Only gate when a priority weapon actually has a fireable target this action; a Deadly weapon
            // with nothing in range / line of sight must not lock out the unit's other weapons.
            bool anyPriorityFireable = weaponOptions
                .Where(option => priorityWeaponKeys.Contains(WeaponProfileKey.For(option.Weapon)))
                .SelectMany(option => option.WeaponTargetStats)
                .Any(stats => stats.UnselectableReason == null && stats.modelsThatCanShoot.Count > 0);

            if (!anyPriorityFireable) return;

            string reason = $"Must fire {string.Join(" / ", prioritySources)} weapons first.";

            foreach (WeaponOption option in weaponOptions)
            {
                if (priorityWeaponKeys.Contains(WeaponProfileKey.For(option.Weapon))) continue;

                for (int i = 0; i < option.WeaponTargetStats.Count; i++)
                {
                    WeaponTargetStats stats = option.WeaponTargetStats[i];
                    if (stats.UnselectableReason != null) continue; // keep a more specific reason (target limit, etc.)
                    option.WeaponTargetStats[i] = stats with { UnselectableReason = reason };
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

        // #276: how many copies of the chosen weapon sit on models that can shoot the chosen target,
        // read from the already-built option stats. A model carrying several copies contributes each
        // of them (same as the pooled count it caps). Internal for tests.
        internal static int CountEligibleCopies(List<WeaponOption> weaponOptions, Weapon chosenWeapon,
            DataBinding<UnitData> targetUnit)
        {
            // #306: the option carrying the chosen PROFILE - a same-named sibling with different rules is
            // a different weapon with its own carriers, and counting its copies would over-size the volley.
            string chosenKey = WeaponProfileKey.For(chosenWeapon);
            WeaponOption? option = weaponOptions
                .FirstOrDefault(o => WeaponProfileKey.For(o.Weapon) == chosenKey);
            WeaponTargetStats? stats = option?.WeaponTargetStats
                .FirstOrDefault(s => s.TargetUnit.Reference.Equals(targetUnit.Reference));
            if (stats == null) return 0;

            var comparer = new WeaponComparer();
            int copies = 0;
            foreach (DataBinding<ModelData> model in stats.modelsThatCanShoot)
            {
                foreach (Weapon weapon in model.Weapons())
                {
                    if (comparer.Equals(weapon, chosenWeapon)) copies++;
                }
            }
            return copies;
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
        public static bool HasAnyFireableTarget(DataBinding<UnitData> attackingUnit, IGameContext gameContext,
            bool markedTargetsOnly = false)
        {
            UnitData unitValue = attackingUnit.GetValue();
            List<Weapon> rangedWeapons = unitValue.GetRangedWeapons();
            if (rangedWeapons.Count == 0) return false;

            // GetRangedWeapons returns one Weapon instance per model that carries it, so duplicates are
            // normal. #306: group by PROFILE through the same helper CombatActionContext builds its
            // AvailableWeapons with - the gate must answer exactly what the stage will conclude (#200),
            // and the old name-dedupe collapsed a same-name split into a single option the stage would
            // never offer. Counts are irrelevant here; only the set of profiles is.
            ConcurrentDictionary<Weapon, int> availableWeapons = WeaponPool.GroupByProfile(rangedWeapons);

            List<ITerrain> terrainSnapshot = gameContext.TableState.Terrain.Objects.ToList();
            List<WeaponOption> options = BuildWeaponOptions(attackingUnit, availableWeapons, gameContext,
                terrainSnapshot, Array.Empty<DataReference>());

            // The stage's own gating pipeline, verbatim (#200): the gate must answer exactly what the
            // stage will conclude, or a deterministic AI ping-pongs between Choose Action and Shoot.
            ApplyTargetGating(options, attackingUnit, gameContext, markedTargetsOnly);
            return HasAnyFireableOption(options);
        }

        private static List<WeaponOption> BuildWeaponOptions(DataBinding<UnitData> attackingUnit,
            IReadOnlyDictionary<Weapon, int> availableWeapons, IGameContext gameContext,
            IReadOnlyList<ITerrain> terrain, IReadOnlyCollection<DataReference> attackedDefenderRefs)
        {
            PlayerID playerID = attackingUnit.PlayerID();

            ITeam playerTeam = gameContext.TableState.Teams.Objects
                .First(team => team.IsPlayerOnTeam(playerID));

            // #306: keyed by weapon PROFILE, not by name. A partial upgrade (one of three rifles bought
            // Precise) puts two same-named, differently-ruled weapons in the pool, and the old
            // name-keyed Add threw on the second - faulting the state machine mid-activation.
            Dictionary<string, WeaponOption> optionsByProfile =
                new Dictionary<string, WeaponOption>(StringComparer.Ordinal);
            // Per-weapon LoS-ignore (Indirect): a weapon that ignores intervening terrain for LoS
            // can hit targets it has no clear line to, so enumeration must not require LoS for it.
            Dictionary<string, bool> weaponIgnoresLineOfSight =
                new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach(Weapon weapon in availableWeapons.Keys)
            {
                // #042: surface per-weapon whether this weapon ignores cover (Blast) or terrain/LoS
                // (Indirect), AND which rule causes it, so the resolver can both know a
                // cover-/LoS-blocked target is still shootable and attribute it to the player ("(Blast
                // ignores cover)"). Derived from the attacker's rules via the shared query (same source the
                // cover and occlusion stages use; the *Source variants are non-logging).
                string? coverIgnoreRule = Rules.Dispatch.SightRuleQueries.CoverIgnoreSource(
                    attackingUnit.GetValue(), weapon, gameContext.RuleEvaluator);
                string? losIgnoreRule = Rules.Dispatch.SightRuleQueries.LineOfSightIgnoreSource(
                    attackingUnit.GetValue(), weapon, gameContext.RuleEvaluator);
                // TryAdd, not Add: the pool is already profile-grouped, so a repeat means the caller
                // handed us an ungrouped list - folding the copies into one option is what grouping
                // would have done anyway, and is never worth throwing over.
                // #319: a once-per-game weapon is named on the option so the resolvers can badge it BEFORE it
                // is fired (they cannot read the weapon's rules themselves - RuleDefinitions is [JsonIgnore],
                // so a remote player's request carries none).
                string? limitedRule = Rules.Dispatch.LimitedRules.LimitedRuleName(weapon);
                bool limitedSpent = limitedRule != null
                    && Rules.Dispatch.LimitedRules.IsSpent(attackingUnit.GetValue(), weapon);
                // #340: a weapon whose attack re-scopes to an individual model (Takedown) fires one copy
                // per pass through this stage, each with its own target. The defender is the neutral
                // attacker stand-in WoundPriorityQueries.ShootingResolveFirstSource uses for the same
                // query - Takedown's condition is unconditional, and this flag is per weapon row, not per
                // target row.
                string? individualTargetRule = Rules.Dispatch.SightRuleQueries.IndividualTargetSource(
                    attackingUnit.GetValue(), weapon, attackingUnit.GetValue(), gameContext.RuleEvaluator);
                string profileKey = WeaponProfileKey.For(weapon);
                if (optionsByProfile.TryAdd(profileKey,
                    new WeaponOption(weapon, new List<WeaponTargetStats>(),
                        coverIgnoreRule != null, losIgnoreRule != null, coverIgnoreRule, losIgnoreRule,
                        limitedRule, limitedSpent,
                        individualTargetRule, availableWeapons[weapon])))
                {
                    weaponIgnoresLineOfSight.Add(profileKey, losIgnoreRule != null);
                }
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
                    BuildAttacksForEnemyUnit(attackingUnit, enemyUnit, optionsByProfile.Keys,
                        terrain, gameContext, weaponIgnoresLineOfSight, unselectableReason);

                foreach (KeyValuePair<string, WeaponTargetStats> kvp in weaponToStats)
                {
                    optionsByProfile[kvp.Key].WeaponTargetStats.Add(kvp.Value);
                }
            }

            // #209: availableWeapons is keyed by the Weapon reference type, so its enumeration
            // order is identity-hash-dependent and varied per run, breaking same-seed replay
            // (#193). Ordering by the profile key is a TOTAL deterministic order - and, being
            // name-primary, the same order the old OrderBy(Weapon.Name) produced whenever names are
            // unique, which is every shipped book today (#306).
            return optionsByProfile
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.Value).ToList();
        }

        // #306: weaponProfileKeys / weaponToStats are keyed by WeaponProfileKey, so a model's plain rifle
        // and its neighbour's upgraded one land in their own rows instead of colliding on the name.
        private static Dictionary<string, WeaponTargetStats> BuildAttacksForEnemyUnit(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> enemyUnit, IEnumerable<string> weaponProfileKeys, IReadOnlyList<ITerrain> terrain,
            IGameContext gameContext, IReadOnlyDictionary<string, bool> weaponIgnoresLineOfSight,
            string? unselectableReason = null)
        {
            Dictionary<string, WeaponTargetStats> weaponToStats =
                new Dictionary<string, WeaponTargetStats>(StringComparer.Ordinal);

            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(
                gameContext.TableState, attackingUnit, enemyUnit);
            IReadOnlyList<ITerrain> allTerrain = terrain.Concat(modelBlockers).ToList();

            bool hasCover = ComputeHasCover(attackingUnit, enemyUnit, allTerrain,
                gameContext.Settings.CoverProximityExceptionsEnabled);

            foreach (string weaponProfileKey in weaponProfileKeys)
            {
                weaponToStats[weaponProfileKey] = new WeaponTargetStats(enemyUnit,
                    new HashSet<DataBinding<ModelData>>(),
                    new HashSet<DataBinding<ModelData>>(),
                    hasCover,
                    unselectableReason);
            }

            //TODO: Cache line of sight lookups.

            // #102: a weapon's effective range against THIS enemy unit folds the attacker's own range buffs
            // (Increased Shooting Range) and this defender's range debuffs (Ranged Shrouding). The delta is the
            // same for every model of the attacking unit (a unit+weapon+defender property), so compute it once
            // per weapon profile and reuse it across the model loop.
            Dictionary<string, float> effectiveRangeByWeapon =
                new Dictionary<string, float>(StringComparer.Ordinal);

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
                    // #306: the model's own copy is matched to its profile's row - by name alone, a model
                    // carrying the plain rifle would have been credited to whichever same-named row won.
                    string weaponProfileKey = WeaponProfileKey.For(weapon);
                    if (weaponToStats.ContainsKey(weaponProfileKey) == false)
                    {
                        continue;
                    }

                    WeaponTargetStats weaponTargetStats = weaponToStats[weaponProfileKey];
                    bool ignoresLoS = weaponIgnoresLineOfSight.TryGetValue(weaponProfileKey, out bool ig) && ig;

                    if (!effectiveRangeByWeapon.TryGetValue(weaponProfileKey, out float effectiveRange))
                    {
                        effectiveRange = Rules.Dispatch.RangeRuleQueries.EffectiveRange(
                            attackingUnit.GetValue(), weapon, enemyUnit.GetValue(), gameContext.RuleEvaluator);
                        effectiveRangeByWeapon[weaponProfileKey] = effectiveRange;
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
                // #042 Indirect: a weapon that ignores intervening terrain for LoS may fire at a
                // target it has no clear line to, so only range gates it. (Takedown does NOT - #314.)
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
            // #042 Indirect's LoS-ignore is handled by the caller (CanWeaponShootAtUnit short-
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
        // #055/#045 truthfulness: this feeds the targeting UI's cover flag, so it must mirror
        // CoverCheckStage exactly — including the #201 proximity exceptions, threaded here as
        // applyProximityExceptions from GameSettings.CoverProximityExceptionsEnabled.
        internal static bool ComputeHasCover(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> defendingUnit, IReadOnlyList<ITerrain> terrain,
            bool applyProximityExceptions)
        {
            List<DataBinding<ModelData>> attackers = attackingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()).ToList();
            List<DataBinding<ModelData>> defenders = defendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()).ToList();
            if (defenders.Count == 0) return false;

            int modelsInCover = 0;
            foreach (DataBinding<ModelData> defender in defenders)
            {
                ModelData defModel = defender.GetValue();
                Position defPos = defModel.PositionBinding.GetValue();
                foreach (DataBinding<ModelData> attacker in attackers)
                {
                    ModelData atkModel = attacker.GetValue();
                    ESightLineEffect effect = LineOfSightUtilities.EvaluateSightLine(
                        atkModel.PositionBinding.GetValue(), defPos, terrain,
                        CoverContext.ForModels(atkModel, defModel), applyProximityExceptions);
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