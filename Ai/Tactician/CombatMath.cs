using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// The situation an attack estimate is computed under. Distance and cover are INPUTS (the caller
    /// supplies the hypothetical position's values), so the same units can be priced "from here" and
    /// "from there" without touching game state - the property movement scoring depends on (#191 A3/A4).
    /// </summary>
    /// <param name="SightFactor">
    /// #363: the fraction of LoS-bound firepower that reaches the defender, given the terrain
    /// between the two hypothetical positions. 1 = clear lane (the default: callers that supply no
    /// geometry get the old distance-only estimate). 0 = the lane is cut and the volley cannot
    /// happen - what an attack FROM a candidate endpoint is worth, since the shot would be taken
    /// from there. Weapons that ignore line of sight (Indirect) are exempt per weapon.
    ///
    /// Fractional values are supported and pinned, but no caller passes one today: #365 removed
    /// the one that did. Discounting an INCOMING volley for terrain was fact-math on a forecast -
    /// the shooter moves before it shoots - and cover is now priced as a bounded habit on the
    /// candidate instead (TacticianWeights.MoveCoverHabit). The fraction stays for #365's Tier 2,
    /// where a lethality gate wants to credit cover deliberately little rather than not at all.
    /// </param>
    public sealed record AttackContext(
        float DistanceInches,
        bool AttackerMoved = false,
        bool DefenderInCover = false,
        bool IsMelee = false,
        bool IsCharging = false,
        float SightFactor = 1f);

    /// <summary>Expected outcome of one unit's whole attack (all weapon batches) against a defender.</summary>
    public sealed record AttackEstimate(
        float ExpectedWounds,
        float ExpectedWoundsUncapped,
        float ExpectedKills,
        bool DestroysUnit,
        IReadOnlyList<string> Notes)
    {
        public static readonly AttackEstimate Zero =
            new(0f, 0f, 0f, false, Array.Empty<string>());
    }

    /// <summary>
    /// Expected outcome of a full melee exchange: impact hits + the charger's swings + the defender's
    /// return strikes, with Counter's strike-first swap, fatigue, and Fear's who-won contribution.
    /// Wounds margin is attacker-perspective: positive means the attacker wins the resolution check.
    /// </summary>
    public sealed record MeleeEstimate(
        AttackEstimate AttackerAttack,
        AttackEstimate DefenderReturn,
        bool DefenderStrikesFirst,
        float AttackerFearBonus,
        float DefenderFearBonus,
        IReadOnlyList<string> Notes)
    {
        public float ExpectedWoundsMargin =>
            AttackerAttack.ExpectedWounds + AttackerFearBonus
            - (DefenderReturn.ExpectedWounds + DefenderFearBonus);
    }

    /// <summary>
    /// Closed-form expected-outcome estimates for the Tactician (#191 A1, plan sec. 8) - what an attack
    /// is WORTH, before deciding to make it.
    /// <para>
    /// Design: this is an arithmetic mirror of the real combat stages (DetermineHitRoll -> RollToHit ->
    /// DetermineSaveRollsNeeded -> RollToSave -> AssignWounds) under the probabilistic roller, but all
    /// RULE effects are delegated to the engine's own <see cref="RuleEvaluator.EvaluateAllNamed"/>
    /// (the read-only query: no logging, no one-shot-grant spending) with the same hook contexts,
    /// participants, and sinks the stages use. So every rule the engine implements - core catalog and
    /// data-authored clones alike - prices itself here with zero name-by-name reimplementation, and the
    /// pin tests (CombatMathPinTests) hold this mirror to the stages' actual output. The engine is
    /// ground truth (plan G1); when they disagree, THIS file is wrong.
    /// </para>
    /// <para>
    /// Known deliberate gaps (also surfaced per-call via Notes; see the A1 ledger entry in #191):
    /// granted one-shot roll-modifier tokens (engine's only accessor consumes them), target Marks
    /// (claiming them mutates tokens), and per-volley casualty carry-over within one unit's attack
    /// (volleys are priced against the defender's current strength and summed).
    /// </para>
    /// </summary>
    public static class CombatMath
    {
        // Roll() is pure math on the probabilistic roller (per-face = count/6); no decisive rolls are
        // ever taken here, so one shared instance is thread-safe.
        private static readonly ProbabilisticDiceRoller Dice = new ProbabilisticDiceRoller();

        /// <summary>
        /// Expected result of the attacker shooting the defender with every ranged weapon batch that
        /// reaches (per <see cref="RangeRuleQueries.EffectiveRange"/>) at the context's distance.
        /// </summary>
        public static AttackEstimate EstimateShooting(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, AttackContext context)
        {
            var notes = new List<string>();
            float total = 0f;

            foreach ((Weapon weapon, int count) in WeaponBatches(attacker.GetValue(), melee: false))
            {
                float reach = RangeRuleQueries.EffectiveRange(
                    attacker.GetValue(), weapon, defender.GetValue(), evaluator);
                if (reach < context.DistanceInches) continue;

                // #363: terrain between the two positions scales every weapon that needs a lane -
                // silenced outright at factor 0, discounted in between (see AttackContext). The
                // geometry is caller-supplied, like DistanceInches, so the estimate itself stays
                // position-free. Indirect keeps firing at full value; the query only runs when the
                // lane is not clear, so clear-lane estimates cost exactly what they always did.
                float sight = Math.Clamp(context.SightFactor, 0f, 1f);
                if (sight < 1f
                    && SightRuleQueries.IgnoresTerrain(attacker.GetValue(), weapon, evaluator))
                {
                    sight = 1f;
                }
                if (sight <= 0f) continue;

                float volley = EstimateVolley(evaluator, attacker, defender, weapon, count,
                    context with { IsMelee = false, IsCharging = false }, notes);
                total += sight < 1f ? sight * volley : volley;
            }

            return Finish(total, defender, notes);
        }

        /// <summary>
        /// Expected result of a damage spell (<see cref="Effect.DealHits"/>) landing on the defender
        /// (#191 A5): the spell's fixed hit count resolved through the save/wound mirror, with the
        /// spell's AP and pre-resolved weapon rules riding a synthetic weapon exactly as
        /// <see cref="CastSpellStage"/> builds one. Returns Zero for a non-damage spell. Not priced
        /// (recorded A5 gap): the stage's hit-complete fold on the fixed hits (Blast multiply, on-6
        /// extras) - the estimate is the raw count through saves.
        /// </summary>
        public static AttackEstimate EstimateSpellDamage(RuleEvaluator evaluator,
            DataBinding<UnitData> caster, DataBinding<UnitData> defender, RuntimeSpell spell)
        {
            if (spell.Effect is not Effect.DealHits dealHits) return AttackEstimate.Zero;

            Weapon spellWeapon = new Weapon(spell.Name, rangeInches: 0f, attacks: 0,
                armorPenetration: dealHits.ArmorPenetration);
            foreach (ResolvedRule rule in spell.WeaponRules)
            {
                spellWeapon.AttachRuleDefinition(rule);
            }

            var notes = new List<string>();
            float wounds = ResolveSaves(evaluator, caster.GetValue(), defender.GetValue(), spellWeapon,
                SingleGroup(dealHits.Count), apReduction: 0, wholeAttackSaveModifier: 0,
                coverBonus: 0, isMelee: false, notes);
            return Finish(wounds, defender, notes);
        }

        /// <summary>
        /// Expected result of a rolled hit pool (<see cref="Effect.DealPooledHits"/>, #197 Surprise
        /// Attack) landing on the defender: <paramref name="diceCount"/> dice, each result at or above
        /// <paramref name="successThreshold"/> becoming one hit at <paramref name="armorPenetration"/>,
        /// resolved through the same save/wound mirror <see cref="EstimateSpellDamage"/> uses. The pool
        /// is rolled on the probabilistic roller, so the expected hit count is fractional exactly as
        /// SurpriseAttackStage's is - this prices the same arithmetic the stage will perform.
        /// </summary>
        public static AttackEstimate EstimatePooledHits(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, string label,
            int diceCount, int successThreshold, int armorPenetration)
        {
            if (diceCount <= 0) return AttackEstimate.Zero;

            Weapon burstWeapon = new Weapon(label, rangeInches: 0f, attacks: 0,
                armorPenetration: armorPenetration);

            float hits = Dice.Roll(diceCount)
                .SubsetAtOrAbove(DiceUtilities.ClampSuccessRollNeeded(successThreshold)).TotalRolls;

            var notes = new List<string>();
            float wounds = ResolveSaves(evaluator, attacker.GetValue(), defender.GetValue(), burstWeapon,
                SingleGroup(hits), apReduction: 0, wholeAttackSaveModifier: 0,
                coverBonus: 0, isMelee: false, notes);
            return Finish(wounds, defender, notes);
        }

        /// <summary>
        /// Expected result of a full melee exchange, attacker charging the defender. Sequencing mirrors
        /// MeleeStage: impact hits first, then Counter's strike-first swap (which also strips the
        /// charge from the charger's swings - the engine's charger loses IsCharging after the swap),
        /// then each side swings every melee weapon batch, the side striking second doing so with the
        /// models the first striker's expected kills removed.
        /// </summary>
        public static MeleeEstimate EstimateMelee(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, float distanceInches = 1f)
        {
            var notes = new List<string>();
            UnitData atk = attacker.GetValue();
            UnitData def = defender.GetValue();

            // --- ResolveImpactHitsStage mirror: charger's Impact(X) minus Counter's reduction, each
            // die a hit on 2+, resolved as a synthetic AP-0 attack before any swing.
            float impactWounds = 0f;
            var contactParticipants = new List<RuleParticipant> { RuleParticipant.Actor(atk) };
            contactParticipants.AddRange(DetermineStrikeOrderStage.SubjectWithMeleeWeapons(def));
            IReadOnlyList<RuleOperation> contactOps = Ops(evaluator.EvaluateAllNamed(
                new ChargeContactContext(atk, def), contactParticipants.ToArray()));
            var impact = new ImpactSink();
            impact.ApplyFrom(contactOps);
            if (impact.TotalDice > 0)
            {
                float impactHits = Dice.Roll(impact.TotalDice).AtOrAbove(2);
                // #355: read the sink's AP like ResolveImpactHitsStage does - hardcoding 0 valued Heavy
                // Impact's AP(1) hits as though they were core Impact's, understating every ram by it.
                impactWounds = ResolveSaves(evaluator, atk, def,
                    new Weapon("Impact", rangeInches: 0f, attacks: 0,
                        armorPenetration: impact.ArmorPenetration),
                    SingleGroup(impactHits), apReduction: 0, wholeAttackSaveModifier: 0,
                    coverBonus: 0, isMelee: true, notes);
            }

            // --- DetermineStrikeOrderStage mirror: a Counter defender with a usable melee weapon takes
            // the first swing, and the charge is spent - nobody counts as charging from here on.
            IReadOnlyList<RuleOperation> counterOps = Ops(evaluator.EvaluateAllNamed(
                new CounterTriggerContext(atk, def),
                DetermineStrikeOrderStage.SubjectWithMeleeWeapons(def)));
            bool defenderStrikesFirst = counterOps.OfType<RuleOperation.StrikeFirst>().Any()
                && def.GetMeleeWeapons().Count > 0;

            var meleeContext = new AttackContext(distanceInches, IsMelee: true,
                IsCharging: !defenderStrikesFirst);

            // --- Swings. The first striker attacks at full strength; the second striker's weapon pool
            // loses the models the first side is expected to kill (probabilistic play kills models only
            // via whole-wound allocation, which is exactly what SurvivorWeaponBatches mirrors).
            float attackerSwings, defenderSwings;
            if (defenderStrikesFirst)
            {
                defenderSwings = SwingAllBatches(evaluator, defender, attacker,
                    WeaponBatches(def, melee: true), meleeContext with { IsCharging = false }, notes);
                float attackerCasualties = defenderSwings;
                attackerSwings = SwingAllBatches(evaluator, attacker, defender,
                    SurvivorWeaponBatches(atk, attackerCasualties), meleeContext, notes);
            }
            else
            {
                attackerSwings = SwingAllBatches(evaluator, attacker, defender,
                    WeaponBatches(atk, melee: true), meleeContext, notes);
                defenderSwings = SwingAllBatches(evaluator, defender, attacker,
                    SurvivorWeaponBatches(def, impactWounds + attackerSwings),
                    meleeContext with { IsCharging = false }, notes);
            }

            // --- DetermineMeleeWinnerStage mirror: Fear(X) adds phantom wounds to its own side's dealt
            // total for the who-won comparison only.
            var resolution = new MeleeResolutionContext(atk, def);
            float attackerFear = SumFear(evaluator, resolution, atk);
            float defenderFear = SumFear(evaluator, resolution, def);

            AttackEstimate attackerEstimate = Finish(impactWounds + attackerSwings, defender, notes);
            AttackEstimate defenderEstimate = Finish(defenderSwings, attacker, Array.Empty<string>());

            return new MeleeEstimate(attackerEstimate, defenderEstimate, defenderStrikesFirst,
                attackerFear, defenderFear, notes);
        }

        /// <summary>
        /// Expected wounds from ONE weapon batch (weapon x count) - the exact scope the engine resolves
        /// as one volley through the hit/save/wound stages. Public because the pin tests hold precisely
        /// this unit of math against the real stages.
        /// </summary>
        public static float EstimateVolley(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            Weapon weapon, int weaponCount, AttackContext context, List<string> notes)
        {
            UnitData atk = attacker.GetValue();
            UnitData def = defender.GetValue();

            // --- DetermineHitRollStage mirror. (Not mirrored: ClaimTargetMarks - claiming mutates
            // tokens - and granted one-shot hit buffs - the engine's only accessor consumes them.)
            NoteIf(def.Tokens.GetAllTokens(TokenType.Mark).Any(),
                "defender carries Mark tokens: unpriced (claiming them mutates state)", notes);

            IReadOnlyList<RuleOperation> hitModOps = Ops(evaluator.EvaluateAllNamed(
                new HitRollModifierContext(atk, def, context.DistanceInches, context.AttackerMoved,
                    context.IsMelee, context.IsCharging),
                ActorWithBatch(atk, weapon),
                RuleParticipant.Subject(def, models: HeroStatRules.LivingModels(def))));

            int baseQuality = HeroStatRules.GetAttackQuality(atk, weapon);
            var qualityFloor = new QualityFloorSink();
            qualityFloor.ApplyFrom(hitModOps);
            if (qualityFloor.HasFloor)
                baseQuality = Math.Min(baseQuality, qualityFloor.Quality);

            float attackCount = weapon.Attacks * weaponCount;
            var hitModifiers = new RollModifierSink();
            hitModifiers.ApplyFrom(hitModOps);
            int hitRollNeeded = baseQuality - hitModifiers.Net(ERollKind.Hit);

            if (context.IsMelee && FatigueUtilities.CountsAsFatiguedInMelee(atk))
                hitRollNeeded = 6; // fatigue overrides every modifier

            // --- RollToHitStage mirror.
            IDiceResults rolls = Dice.Roll(attackCount);
            IDiceResults successful = rolls.SubsetAtOrAbove(DiceUtilities.ClampSuccessRollNeeded(hitRollNeeded));

            IReadOnlyList<RuleOperation> completeOps = Ops(evaluator.EvaluateAllNamed(
                new HitRollCompleteContext(atk, def, rolls, context.DistanceInches,
                    context.IsMelee, context.IsCharging),
                ActorWithBatch(atk, weapon),
                RuleParticipant.Subject(def, models: HeroStatRules.LivingModels(def))));

            List<SuccessfulHitInfo> groups = PerHitApSplitter.Split(successful, completeOps);

            var hitInjection = new HitInjectionSink();
            hitInjection.ApplyFrom(completeOps);
            if (hitInjection.TotalExtraHits > 0f)
                groups.Add(SingleGroupInfo(hitInjection.TotalExtraHits));

            var hitMultiplier = new HitMultiplierSink();
            hitMultiplier.ApplyFrom(completeOps);
            if (hitMultiplier.NetMultiplier > 1)
            {
                // Mirrors RollToHitStage: the model-count cap is PER HIT and the multiplied hits stack,
                // so it trims the MULTIPLIER, not the volley's total.
                float currentHits = groups.Sum(group => group.HitCount);
                int livingDefenders = def.Models.Count(model => model.GetIsAlive());
                int effectiveMultiplier = Math.Max(1, Math.Min(hitMultiplier.NetMultiplier, livingDefenders));
                float cappedHits = currentHits * effectiveMultiplier;
                if (cappedHits - currentHits > 0f)
                    groups.Add(SingleGroupInfo(cappedHits - currentHits));
            }

            var saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(completeOps);
            int apReduction = completeOps.OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

            // --- CoverCheckStage stand-in: cover is a caller-supplied fact about the hypothetical
            // position; the ignore rules (Blast/Indirect/Takedown) are still the engine's own query.
            int coverBonus = !context.IsMelee && context.DefenderInCover
                && !SightRuleQueries.IgnoresCover(atk, weapon, evaluator) ? 1 : 0;

            return ResolveSaves(evaluator, atk, def, weapon, groups, apReduction,
                saveModifiers.Net(ERollKind.Save), coverBonus, context.IsMelee, notes);
        }

        // --- DetermineSaveRollsNeeded + RollToSave + AssignWounds mirror (shared by volleys and
        // impact hits). Returns the expected wound total BEFORE capping at the defender's remaining.
        private static float ResolveSaves(RuleEvaluator evaluator, UnitData atk, UnitData def,
            Weapon weapon, List<SuccessfulHitInfo> groups, int apReduction, int wholeAttackSaveModifier,
            int coverBonus, bool isMelee, List<string> notes)
        {
            int baseDefense = HeroStatRules.GetSaveDefense(def);
            int ap = Math.Max(0, weapon.ArmorPenetration - apReduction);
            // Granted "+X to defense" tokens are skipped: the engine's only accessor consumes them.
            int baseDefenseWithAP = baseDefense + ap - coverBonus - wholeAttackSaveModifier;

            float totalWounds = 0f;
            float[] combinedSaveFaces = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            var savedGroups = new List<(IDiceResults Saved, int SaveNeeded)>();

            foreach (SuccessfulHitInfo hits in groups)
            {
                int saveNeeded = DiceUtilities.ClampSuccessRollNeeded(baseDefenseWithAP - hits.SaveModifier);
                IDiceResults saveRolls = Dice.Roll(hits.HitCount);
                totalWounds += saveRolls.Below(saveNeeded);

                IDiceResults saved = saveRolls.SubsetAtOrAbove(saveNeeded);
                savedGroups.Add((saved, saveNeeded));
                for (int face = saveRolls.SideMin; face <= saveRolls.SideMax; face++)
                    combinedSaveFaces[face - 1] += saveRolls.At(face);
            }

            // --- AssignWoundsStage mirror, in its load-bearing order: Bane reroll -> Deadly clump
            // confinement -> Shred injection -> Regeneration ignore.
            IReadOnlyList<RuleOperation> saveCompleteOps = Ops(evaluator.EvaluateAllNamed(
                new SaveRollCompleteContext(atk, def, new DiceResults(combinedSaveFaces), isMelee),
                RuleParticipant.Actor(atk, weapon),
                RuleParticipant.Subject(def, models: HeroStatRules.LivingModels(def))));

            var reroll = new RerollSink();
            reroll.ApplyFrom(saveCompleteOps);
            // Mirrors AssignWoundsStage: the reroll threshold is the unmodified max (6) unless a Boost
            // variant widened it to 5-6, and the same clamp keeps an out-of-range authoring honest.
            if (reroll.RerollSavesAtOrAbove is int rerollFrom)
            {
                foreach ((IDiceResults saved, int saveNeeded) in savedGroups)
                {
                    int threshold = System.Math.Clamp(rerollFrom, saved.SideMin, saved.SideMax);
                    float qualifying = saved.AtOrAbove(threshold);
                    if (qualifying <= 0f) continue;
                    totalWounds += Dice.Roll(qualifying).Below(saveNeeded);
                }
            }

            IReadOnlyList<RuleOperation> woundOps = Ops(evaluator.EvaluateAllNamed(
                new PreApplyWoundContext(atk, def), RuleParticipant.Actor(atk, weapon)));
            var woundMultiplier = new WoundModifierSink();
            woundMultiplier.ApplyFrom(woundOps);
            if (woundMultiplier.NetMultiplier > 1)
                totalWounds = ConfineToClumps(totalWounds, woundMultiplier.NetMultiplier, def);

            var woundInjection = new WoundInjectionSink();
            woundInjection.ApplyFrom(saveCompleteOps);
            totalWounds += woundInjection.TotalExtraWounds;

            var woundIgnore = new WoundIgnoreSink();
            woundIgnore.ApplyFrom(saveCompleteOps);
            if (woundIgnore.HasIgnore && totalWounds > 0f)
                totalWounds -= Dice.Roll(totalWounds).AtOrAbove(woundIgnore.Threshold);

            // Takedown re-scopes the attack to one chosen model (BuildTargetListStage); estimate the
            // best case: confined to the healthiest living model, overkill lost.
            if (HasTargetIndividualModel(weapon))
            {
                float bestRemaining = def.Models.Where(model => model.GetIsAlive())
                    .Select(model => model.TotalWounds - model.WoundsDealt).DefaultIfEmpty(0f).Max();
                float confined = Math.Min(totalWounds, bestRemaining);
                NoteIf(true, $"{weapon.Name}: Takedown priced against the healthiest single model", notes);
                return confined;
            }

            return totalWounds;
        }

        // Deadly's no-carry-over confinement - the exact algorithm of AssignWoundsStage.ConfineToClumps
        // (private there; pinned against it by the Deadly pin tests).
        private static float ConfineToClumps(float clumpCount, int multiplier, IUnit defender)
        {
            float effective = 0f;
            float remainingClumps = clumpCount;

            foreach (IModel model in defender.Models)
            {
                if (remainingClumps <= 0f) break;
                if (!model.GetIsAlive()) continue;

                float capacity = model.TotalWounds - model.WoundsDealt;
                if (capacity <= 0f) continue;

                float clumpsToKill = MathF.Ceiling(capacity / multiplier);
                float used = MathF.Min(remainingClumps, clumpsToKill);
                effective += MathF.Min(used * multiplier, capacity);
                remainingClumps -= used;
            }

            return effective;
        }

        // --- Allocation mirror: wounds fill already-wounded models first, then whole models in unit
        // order, joined hero last (AssignWoundsResults' mandatory ordering). Returns expected kills.
        private static float ExpectedKills(UnitData defender, float wounds, out bool destroysUnit)
        {
            destroysUnit = wounds >= defender.RemainingWounds && defender.RemainingWounds > 0f;

            float kills = 0f;
            float pool = wounds;
            foreach (IModel model in AllocationOrder(defender))
            {
                if (pool <= 0f) break;
                float remaining = model.TotalWounds - model.WoundsDealt;
                if (remaining <= 0f) continue;
                if (pool >= remaining) { kills += 1f; pool -= remaining; }
                else break; // a partially wounded model still stands (and still fights)
            }
            return kills;
        }

        private static IEnumerable<IModel> AllocationOrder(UnitData defender)
        {
            ModelID? heroId = defender.HeroAttachment?.HeroModelId;
            List<IModel> living = defender.Models.Where(model => model.GetIsAlive()).ToList();
            bool IsHero(IModel model) => heroId.HasValue && model.ID.Equals(heroId.Value);

            foreach (IModel model in living.Where(model => model.WoundsDealt > 0f && !IsHero(model)))
                yield return model;
            foreach (IModel model in living.Where(model => model.WoundsDealt <= 0f && !IsHero(model)))
                yield return model;
            foreach (IModel model in living.Where(IsHero))
                yield return model;
        }

        // --- Weapon batching: living models' weapons grouped by stat-identity (WeaponComparer), the
        // same pooling CombatActionContext does. Melee estimates assume every living carrier is in
        // range post pile-in (noted approximation).
        private static List<(Weapon Weapon, int Count)> WeaponBatches(UnitData unit, bool melee)
        {
            List<Weapon> weapons = melee ? unit.GetMeleeWeapons() : unit.GetRangedWeapons();
            return Batch(weapons);
        }

        // The second striker's pool: its living models minus the first striker's expected whole kills,
        // removed in allocation order (the same models ExpectedKills counts as dead).
        private static List<(Weapon Weapon, int Count)> SurvivorWeaponBatches(UnitData unit, float incomingWounds)
        {
            var dead = new HashSet<IModel>(ReferenceEqualityComparer.Instance);
            float pool = incomingWounds;
            foreach (IModel model in AllocationOrder(unit))
            {
                float remaining = model.TotalWounds - model.WoundsDealt;
                if (remaining <= 0f || pool < remaining) break;
                dead.Add(model);
                pool -= remaining;
            }

            var weapons = new List<Weapon>();
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive() || dead.Contains(model)) continue;
                // #197 Strafing: strafe-only weapons never swing (mirrors GetMeleeWeapons, which the
                // full-strength batch above goes through).
                weapons.AddRange(model.Weapons.Where(weapon =>
                    weapon.IsMelee() && !StrafingRules.IsStrafeOnly(weapon)));
            }
            return Batch(weapons);
        }

        private static List<(Weapon Weapon, int Count)> Batch(List<Weapon> weapons)
        {
            var comparer = new WeaponComparer();
            var batches = new List<(Weapon Weapon, int Count)>();
            foreach (Weapon weapon in weapons)
            {
                int index = batches.FindIndex(batch => comparer.Equals(batch.Weapon, weapon));
                if (index >= 0) batches[index] = (batches[index].Weapon, batches[index].Count + 1);
                else batches.Add((weapon, 1));
            }
            return batches;
        }

        private static float SwingAllBatches(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            List<(Weapon Weapon, int Count)> batches, AttackContext context, List<string> notes)
        {
            float total = 0f;
            foreach ((Weapon weapon, int count) in batches)
                total += EstimateVolley(evaluator, attacker, defender, weapon, count, context, notes);
            return total;
        }

        private static float SumFear(RuleEvaluator evaluator, MeleeResolutionContext resolution, UnitData actor)
        {
            return Ops(evaluator.EvaluateAllNamed(resolution, RuleParticipant.Actor(actor)))
                .OfType<RuleOperation.ExtraMeleeWoundCount>().Sum(op => op.Amount);
        }

        /// <summary>
        /// Expected whole-model kills from this many wounds into the defender, via the engine's
        /// own allocation order (#191 A5-4 - mob-break pricing needs kills, not just wounds).
        /// </summary>
        public static float ExpectedKillsFrom(UnitData defender, float wounds)
            => ExpectedKills(defender, Math.Min(wounds, defender.RemainingWounds), out _);

        private static AttackEstimate Finish(float totalWounds, DataBinding<UnitData> defender,
            IReadOnlyList<string> notes)
        {
            float remaining = defender.GetValue().RemainingWounds;
            float capped = Math.Min(totalWounds, remaining);
            float kills = ExpectedKills(defender.GetValue(), capped, out bool destroys);
            return new AttackEstimate(capped, totalWounds, kills, destroys, notes);
        }

        private static RuleParticipant ActorWithBatch(UnitData attacker, Weapon weapon) =>
            RuleParticipant.Actor(attacker, weapon,
                HeroStatRules.LivingWeaponBatchOwners(attacker, weapon), EModelRuleScope.AllOwners);

        private static bool HasTargetIndividualModel(Weapon weapon) =>
            weapon.RuleDefinitions.Any(rule => rule.Definition.Passive
                .Any(entry => entry.Effect is Effect.TargetIndividualModel));

        private static List<SuccessfulHitInfo> SingleGroup(float hitCount) =>
            new() { SingleGroupInfo(hitCount) };

        // Mirrors RollToHitStage.SyntheticHits: a scalar count at the top face, base AP.
        private static SuccessfulHitInfo SingleGroupInfo(float hitCount)
        {
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[^1] = hitCount;
            return new SuccessfulHitInfo(new DiceResults(perSide));
        }

        private static IReadOnlyList<RuleOperation> Ops(
            IReadOnlyList<(RuleOperation Op, string RuleName)> named) =>
            named.Select(pair => pair.Op).ToList();

        private static void NoteIf(bool condition, string note, List<string> notes)
        {
            if (condition && !notes.Contains(note)) notes.Add(note);
        }
    }
}
