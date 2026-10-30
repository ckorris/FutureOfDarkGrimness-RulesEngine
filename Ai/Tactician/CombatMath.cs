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
    /// <param name="ChargeOriginInches">
    /// #442 (#432's charge arms): how far, base to base, the charger stood from this defender when its activation
    /// began - what the engine snapshots as <c>CombatMetadata.ChargeOriginDistanceInches</c>. Read only for a
    /// charging melee swing (a strike-back or a Countered charge has none, as in the engine), and only by the
    /// "charges / is charged from over 6"" rules (Ferocious's extra hit, a Changebound defender's -1, ...).
    /// 0 = unknown / not a charge: no charge arm fires, the pre-#442 behaviour.
    /// </param>
    public sealed record AttackContext(
        float DistanceInches,
        bool AttackerMoved = false,
        bool DefenderInCover = false,
        bool IsMelee = false,
        bool IsCharging = false,
        float SightFactor = 1f,
        float ChargeOriginInches = 0f);

    /// <summary>Expected outcome of one unit's whole attack (all weapon batches) against a defender.</summary>
    /// <param name="ExpectedCountedWounds">#442 (#431 Scourge): wounds the defender only COUNTS as having taken - for
    /// who won a melee and for whether a shooting attack's wounds trigger a Morale Test - folded from the same
    /// save-complete operations <c>AssignWoundsStage</c> banks (<c>CountedWounds</c>). Never dealt.</param>
    /// <param name="ExpectedSelfWounds">#442 (#434 Hazardous): wounds the ATTACKER is expected to take from its own
    /// attack - each unmodified 1 to hit worth one wound, or the weapon's Blast(X) / Deadly(X), landing on one model
    /// with any excess lost (so a die is worth at most one model's wounds). Not capped by anything else.</param>
    public sealed record AttackEstimate(
        float ExpectedWounds,
        float ExpectedWoundsUncapped,
        float ExpectedKills,
        bool DestroysUnit,
        IReadOnlyList<string> Notes,
        float ExpectedCountedWounds = 0f,
        float ExpectedSelfWounds = 0f)
    {
        public static readonly AttackEstimate Zero =
            new(0f, 0f, 0f, false, Array.Empty<string>());
    }

    /// <summary>
    /// Expected outcome of a full melee exchange: impact hits + the charger's swings + the defender's
    /// return strikes, with Counter's strike-first swap, fatigue, and Fear's who-won contribution.
    /// Wounds margin is attacker-perspective: positive means the attacker wins the resolution check.
    /// </summary>
    /// <param name="AttackerWoundsTakenBefore">#442 (#419): wounds the attacker already took earlier in THIS activation
    /// (<c>WoundsTakenThisActivation</c> - a Dangerous Terrain test, a Breath Attack, a spell), which the 3.6 melee
    /// result counts too. 0 when nothing happened before the estimate.</param>
    /// <param name="DefenderWoundsTakenBefore">The same ledger, read off the defender.</param>
    public sealed record MeleeEstimate(
        AttackEstimate AttackerAttack,
        AttackEstimate DefenderReturn,
        bool DefenderStrikesFirst,
        float AttackerFearBonus,
        float DefenderFearBonus,
        IReadOnlyList<string> Notes,
        float AttackerWoundsTakenBefore = 0f,
        float DefenderWoundsTakenBefore = 0f)
    {
        /// <summary>
        /// DetermineMeleeWinnerStage's comparison, attacker-perspective (positive = the defender took more and loses):
        /// each side's taken total is the wounds dealt to it, the wounds it only counts as taken (#431 Scourge), what it
        /// took earlier in the activation (#419) and the enemy's Fear (#419 3.6 wording).
        /// </summary>
        public float ExpectedWoundsMargin =>
            AttackerAttack.ExpectedWounds + AttackerAttack.ExpectedCountedWounds + DefenderWoundsTakenBefore
                + AttackerFearBonus
            - (DefenderReturn.ExpectedWounds + DefenderReturn.ExpectedCountedWounds + AttackerWoundsTakenBefore
                + DefenderFearBonus);
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
    /// target Marks (claiming them mutates tokens), and per-volley casualty carry-over within one unit's attack
    /// (volleys are priced against the defender's current strength and summed). #442: granted roll-modifier
    /// tokens and the Target / Spotter / Tag markers are priced (read with the non-consuming peeks), and so are
    /// the 3.6 range arms (shooting distance and charge origin), Scourge's counted wounds, Hazardous self-wounds
    /// and the charger's Unwieldy.
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
        /// <para>#442: <paramref name="hypotheticalAttackerRule"/> prices the attack AS IF the attacker also had that
        /// rule (see <see cref="HypotheticalCarrier"/>) - what an activation pick like Versatile Expert's would buy,
        /// asked before the pick is made. Null (every other caller) changes nothing.</para>
        public static AttackEstimate EstimateShooting(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, AttackContext context,
            ResolvedRule? hypotheticalAttackerRule = null)
        {
            var __probe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            try
            {
            Weapon? carrier = HypotheticalCarrier(hypotheticalAttackerRule);
            var notes = new List<string>();
            var tally = new Tally();
            float total = 0f;
            // #191 search perf pass 5: one volley context per estimate, not one per weapon batch.
            AttackContext volleyContext = context.IsMelee || context.IsCharging
                ? context with { IsMelee = false, IsCharging = false }
                : context;

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
                // #425: Indirect ignores line of sight only on a Hold - the context's AttackerMoved is the
                // candidate's action, so an Advance plan's Indirect volley needs the lane like any other.
                if (sight < 1f
                    && SightRuleQueries.IgnoresTerrain(attacker.GetValue(), weapon, evaluator, context.AttackerMoved))
                {
                    sight = 1f;
                }
                if (sight <= 0f) continue;

                float countedBefore = tally.Counted;
                float volley = EstimateVolleyCore(evaluator, attacker, defender, weapon, count,
                    volleyContext, notes, carrier, tally);
                if (sight < 1f) tally.Counted = countedBefore + sight * (tally.Counted - countedBefore);
                total += sight < 1f ? sight * volley : volley;
            }

            return Finish(total, defender, notes, tally.Counted, tally.SelfWounds);
        }
            finally { global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.Combat, __probe); }
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
            var __probe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            try
            {
            if (spell.Effect is not Effect.DealHits dealHits) return AttackEstimate.Zero;

            Weapon spellWeapon = new Weapon(spell.Name, rangeInches: 0f, attacks: 0,
                armorPenetration: dealHits.ArmorPenetration);
            foreach (ResolvedRule rule in spell.WeaponRules)
            {
                spellWeapon.AttachRuleDefinition(rule);
            }

            var notes = new List<string>();
            // #442: a spell's wounds resolve with IsSpell set (ResolveSpellDamageStage), so the save-side spell arms
            // price - Spell Resistance / Knightborn's "spell wounds ignored on 2+" - and the shooting arms do not.
            float wounds = ResolveSaves(evaluator, caster.GetValue(), defender.GetValue(), spellWeapon,
                SingleGroup(dealHits.Count), apReduction: 0, wholeAttackSaveModifier: 0,
                coverBonus: 0, isMelee: false, notes, isSpell: true);
            return Finish(wounds, defender, notes);
        }
            finally { global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.Combat, __probe); }
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
            var __probe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            try
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
            finally { global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.Combat, __probe); }
        }

        /// <summary>
        /// Expected result of a full melee exchange, attacker charging the defender. Sequencing mirrors
        /// MeleeStage: impact hits first (net of Brace), then Counter's strike-first swap (which also strips the
        /// charge from the charger's swings - the engine's charger loses IsCharging after the swap),
        /// then each side swings every melee weapon batch, the side striking second doing so with the
        /// models the first striker's expected kills removed.
        /// </summary>
        /// <para>#442: <paramref name="hypotheticalAttackerRule"/> prices the charger's swings as if it also had that
        /// rule (see <see cref="EstimateShooting"/>); the defender's return strikes and the impact hits never see it.</para>
        /// <para>#442: <paramref name="chargeOriginInches"/> is where the charge was launched from (see
        /// <see cref="AttackContext.ChargeOriginInches"/>); it reaches the charger's swings only, exactly as the engine's
        /// CombatMetadata carries it. 0 (unknown) prices no charge arm.</para>
        public static MeleeEstimate EstimateMelee(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, float distanceInches = 1f,
            float chargeOriginInches = 0f, ResolvedRule? hypotheticalAttackerRule = null)
        {
            var __probe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            try
            {
            Weapon? carrier = HypotheticalCarrier(hypotheticalAttackerRule);
            var notes = new List<string>();
            UnitData atk = attacker.GetValue();
            UnitData def = defender.GetValue();

            // --- ResolveImpactHitsStage mirror: charger's Impact(X) minus the defender's Brace(X), each
            // die a hit on 2+, resolved as a synthetic AP-0 attack before any swing. Both rules' 3.6 Fatigue
            // clauses (#422) are entry conditions, so the shared evaluation below already drops a Fatigued
            // charger's dice and a Fatigued defender's Brace - nothing to mirror by hand. #447: the charger
            // joins with its living models, as in the stage (ChargerParticipant), so Impact sums per model -
            // a joined hero's own Impact included - through the same effect the stage runs.
            float impactWounds = 0f;
            var attackerTally = new Tally();
            var defenderTally = new Tally();
            // #191 search perf pass 5: the defender's melee participants once (two dispatches read them).
            RuleParticipant[] defenderMelee = DetermineStrikeOrderStage.SubjectWithMeleeWeapons(def);
            var contactParticipants = new RuleParticipant[defenderMelee.Length + 1];
            contactParticipants[0] = ResolveImpactHitsStage.ChargerParticipant(atk);
            Array.Copy(defenderMelee, 0, contactParticipants, 1, defenderMelee.Length);
            IReadOnlyList<RuleOperation> contactOps = Ops(evaluator.EvaluateAllNamed(
                new ChargeContactContext(atk, def), contactParticipants));
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
                    coverBonus: 0, isMelee: true, notes, tally: attackerTally);
            }

            // --- DetermineStrikeOrderStage mirror: a Counter defender with a usable melee weapon takes
            // the first swing, and the charge is spent - nobody counts as charging from here on. A Fatigued
            // (or Shaken) defender's Counter fails its own condition in this evaluation (#422). #442 (#431): the
            // charger joins with its melee weapons as in the stage, so an Unwieldy charger (StrikeLast - weapon-scoped
            // in 3.6, or the unit-scoped grant) swaps the order the same way.
            RuleParticipant[] chargerMelee = DetermineStrikeOrderStage.ActorWithMeleeWeapons(atk);
            var strikeOrderParticipants = new RuleParticipant[chargerMelee.Length + defenderMelee.Length];
            Array.Copy(chargerMelee, strikeOrderParticipants, chargerMelee.Length);
            Array.Copy(defenderMelee, 0, strikeOrderParticipants, chargerMelee.Length, defenderMelee.Length);
            IReadOnlyList<RuleOperation> counterOps = Ops(evaluator.EvaluateAllNamed(
                new CounterTriggerContext(atk, def), strikeOrderParticipants));
            bool anyStrikeFirst = false;
            for (int i = 0; i < counterOps.Count && !anyStrikeFirst; i++)
            {
                anyStrikeFirst = counterOps[i] is RuleOperation.StrikeFirst or RuleOperation.StrikeLast;
            }
            bool defenderStrikesFirst = anyStrikeFirst && def.GetMeleeWeapons().Count > 0;

            var meleeContext = new AttackContext(distanceInches, IsMelee: true,
                IsCharging: !defenderStrikesFirst, ChargeOriginInches: chargeOriginInches);

            // --- ResolveRavageWoundsStage mirror (#442, #434): each striker, right before its own swings, rolls its
            // auto-wound pools - Ravage at 6+, Regenerative Strength's tokens at 4+ - once per round (a striker already
            // stamped StrikeDiceUsed rolls none) and not while Fatigued (each rule's own condition, in the
            // evaluation). The attacker's pools come out of the very charge-contact evaluation above (the stage's
            // participant shape); the defender's twin is evaluated with the roles reversed, as StrikeBackStage runs it.
            // Unsaveable: the wounds enter wound assignment pre-failed. Not mirrored: carriers the first striker kills
            // still roll (the pool is priced at full strength).
            float attackerAuto = AutoWounds(evaluator, atk, def, contactOps, notes, attackerTally);
            float DefenderAuto() => AutoWounds(evaluator, def, atk, Ops(evaluator.EvaluateAllNamed(
                new ChargeContactContext(def, atk), DefenderStrikerParticipants(def, atk))), notes, defenderTally);

            // --- Swings. The first striker attacks at full strength; the second striker's weapon pool
            // loses the models the first side is expected to kill (probabilistic play kills models only
            // via whole-wound allocation, which is exactly what SurvivorWeaponBatches mirrors).
            float attackerSwings, defenderSwings, defenderAuto;
            if (defenderStrikesFirst)
            {
                defenderAuto = DefenderAuto();
                defenderSwings = SwingAllBatches(evaluator, defender, attacker,
                    WeaponBatches(def, melee: true), meleeContext with { IsCharging = false }, notes,
                    tally: defenderTally);
                float attackerCasualties = defenderAuto + defenderSwings;
                attackerSwings = SwingAllBatches(evaluator, attacker, defender,
                    SurvivorWeaponBatches(atk, attackerCasualties), meleeContext, notes, carrier, attackerTally);
            }
            else
            {
                attackerSwings = SwingAllBatches(evaluator, attacker, defender,
                    WeaponBatches(atk, melee: true), meleeContext, notes, carrier, attackerTally);
                defenderAuto = DefenderAuto();
                defenderSwings = SwingAllBatches(evaluator, defender, attacker,
                    SurvivorWeaponBatches(def, impactWounds + attackerAuto + attackerSwings),
                    meleeContext with { IsCharging = false }, notes, tally: defenderTally);
            }

            // --- DetermineMeleeWinnerStage mirror: Fear(X) makes the ENEMY count as having taken X more wounds
            // for the who-won comparison only (#419, 3.6 wording) - the same margin as the 3.5 "+X dealt". #442: the
            // wounds each side already took earlier in the activation (#419's ledger) and the wounds it only counts
            // as taken (#431 Scourge) ride the estimate too, so ExpectedWoundsMargin is the engine's comparison.
            var resolution = new MeleeResolutionContext(atk, def);
            float attackerFear = SumFear(evaluator, resolution, atk);
            float defenderFear = SumFear(evaluator, resolution, def);

            AttackEstimate attackerEstimate = Finish(impactWounds + attackerAuto + attackerSwings, defender, notes,
                attackerTally.Counted);
            AttackEstimate defenderEstimate = Finish(defenderAuto + defenderSwings, attacker, Array.Empty<string>(),
                defenderTally.Counted);

            return new MeleeEstimate(attackerEstimate, defenderEstimate, defenderStrikesFirst,
                attackerFear, defenderFear, notes,
                WoundsTakenThisActivation.Peek(atk), WoundsTakenThisActivation.Peek(def));
        }
            finally { global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.Combat, __probe); }
        }

        /// <summary>
        /// Expected wounds from ONE weapon batch (weapon x count) - the exact scope the engine resolves
        /// as one volley through the hit/save/wound stages. Public because the pin tests hold precisely
        /// this unit of math against the real stages.
        /// </summary>
        public static float EstimateVolley(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            Weapon weapon, int weaponCount, AttackContext context, List<string> notes,
            ResolvedRule? hypotheticalAttackerRule = null)
            => EstimateVolleyCore(evaluator, attacker, defender, weapon, weaponCount, context, notes,
                HypotheticalCarrier(hypotheticalAttackerRule));

        /// <summary>
        /// #442 (#431 Scourge): <see cref="EstimateVolley"/> plus the wounds the defender only COUNTS as taken - the
        /// figure the shooting morale check allocates virtually (<c>MoraleUtilities.WoundsLeftUnitAtHalfStrength</c>).
        /// </summary>
        public static (float Wounds, float CountedWounds, float SelfWounds) EstimateVolleyWithCounted(
            RuleEvaluator evaluator, DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            Weapon weapon, int weaponCount, AttackContext context, List<string> notes)
        {
            var tally = new Tally();
            float wounds = EstimateVolleyCore(evaluator, attacker, defender, weapon, weaponCount, context, notes,
                carrier: null, tally);
            return (wounds, tally.Counted, tally.SelfWounds);
        }

        // #442: wounds an attack only makes its target COUNT as taken (#431 Scourge), summed across the volleys and
        // save passes of one estimate - the attack-wide figure the engine banks in CountedWounds.
        private sealed class Tally
        {
            public float Counted;
            // #442 (#434 Hazardous): the attacker's own expected self-wounds across the estimate's volleys.
            public float SelfWounds;
        }

        private static float EstimateVolleyCore(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            Weapon weapon, int weaponCount, AttackContext context, List<string> notes, Weapon? carrier,
            Tally? tally = null)
        {
            UnitData atk = attacker.GetValue();
            UnitData def = defender.GetValue();
            var __hitProbe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();

            // --- DetermineHitRollStage mirror. (Not mirrored: ClaimTargetMarks - claiming mutates tokens. #442:
            // granted hit buffs and the Target/Spotter/Tag markers ARE mirrored now, read with PeekNet / counts.)
            NoteIf(def.Tokens.GetAllTokens(TokenType.Mark).Any(),
                "defender carries Mark tokens: unpriced (claiming them mutates state)", notes);

            // #191 search perf pass 5: the defender's living models once per volley (three dispatches
            // read them), and the sinks only when a rule actually produced an operation - an empty
            // operation list leaves every sink at its default (no floor, net 0, multiplier 1, ...).
            IReadOnlyList<IModel> defenderModels = HeroStatRules.LivingModels(def);
            // #191 search perf: the attacker's batch participant once per volley as well - both
            // dispatches below want the same (unit, weapon) batch, and gathering its living owners is
            // the single largest allocation in the estimate.
            var __modsProbe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            RuleParticipant attackerBatch = ActorWithBatch(atk, weapon);
            RuleParticipant defenderSeat = RuleParticipant.Subject(def, models: defenderModels);
            // #442: only a charging melee swing carries a launch distance (CombatActionContext reports 0 otherwise).
            float chargeOrigin = context.IsMelee && context.IsCharging ? context.ChargeOriginInches : 0f;
            var hitModContext = new HitRollModifierContext(atk, def, context.DistanceInches, context.AttackerMoved,
                context.IsMelee, context.IsCharging, ChargeOriginDistanceInches: chargeOrigin);
            IReadOnlyList<RuleOperation> hitModOps = Ops(carrier == null
                ? evaluator.EvaluateAllNamed(hitModContext, attackerBatch, defenderSeat)
                : evaluator.EvaluateAllNamed(hitModContext, attackerBatch, defenderSeat,
                    RuleParticipant.Actor(atk, carrier)));

            global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.VolleyMods, __modsProbe);

            int baseQuality = HeroStatRules.GetAttackQuality(atk, weapon);
            int hitNet = 0;
            bool ignoresNegative = false;
            if (hitModOps.Count > 0)
            {
                var qualityFloor = new QualityFloorSink();
                qualityFloor.ApplyFrom(hitModOps);
                if (qualityFloor.HasFloor)
                    baseQuality = Math.Min(baseQuality, qualityFloor.Quality);

                var hitModifiers = new RollModifierSink();
                hitModifiers.ApplyFrom(hitModOps);
                hitNet = hitModifiers.Net(ERollKind.Hit);
                for (int i = 0; i < hitModOps.Count && !ignoresNegative; i++)
                    ignoresNegative = hitModOps[i] is RuleOperation.IgnoreNegativeWeaponModifiers;
            }

            float attackCount = weapon.Attacks * weaponCount;
            int hitRollNeeded = baseQuality - hitNet;

            bool fatiguedInMelee = context.IsMelee && FatigueUtilities.CountsAsFatiguedInMelee(atk);
            if (!fatiguedInMelee)
            {
                // #442 (#427 / #434): DetermineHitRollStage's two token folds, read without spending - the attacker's
                // granted "+X to hit" (#033; Unstoppable leaves out another unit's negative grant) and the attacker-
                // bonus markers on the defender (TargetMarkerSpend: persistent Target markers, the spendable Spotter
                // markers and one Tag token, which the AI always claims - the prompts' aggressive default).
                hitRollNeeded -= GrantedRollModifiers.PeekNet(atk, ERollKind.Hit,
                    ignoreOtherUnitsNegative: ignoresNegative);
                hitRollNeeded -= MarkerNet(def, ERollKind.Hit);
            }

            if (fatiguedInMelee)
                hitRollNeeded = 6; // fatigue overrides every modifier

            // --- RollToHitStage mirror.
            IDiceResults rolls = Dice.Roll(attackCount);
            IDiceResults successful = rolls.SubsetAtOrAbove(DiceUtilities.ClampSuccessRollNeeded(hitRollNeeded));

            // #418: the same fatigue decision the engine stamps on its hit context, so a fatigued melee
            // volley's 6s price no Furious/Surge extra hits and no Rending AP, exactly as they will roll.
            var hitContext = new HitRollCompleteContext(atk, def, rolls, context.DistanceInches,
                context.IsMelee, context.IsCharging, ChargeOriginDistanceInches: chargeOrigin,
                SixesCountForSpecialRules: FatigueUtilities.SixesCountForSpecialRules(atk, context.IsMelee),
                AttackerMoved: context.AttackerMoved);

            var __completeProbe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            IReadOnlyList<RuleOperation> completeOps = Ops(carrier == null
                ? evaluator.EvaluateAllNamed(hitContext, attackerBatch, defenderSeat)
                : evaluator.EvaluateAllNamed(hitContext, attackerBatch, defenderSeat,
                    RuleParticipant.Actor(atk, carrier)));

            // With no operations the splitter returns exactly this one-group list.
            List<SuccessfulHitInfo> groups = completeOps.Count == 0
                ? new List<SuccessfulHitInfo> { new SuccessfulHitInfo(successful) }
                : PerHitApSplitter.Split(successful, completeOps, hitContext.SpecialRuleHitRolls);

            int saveNet = 0;
            int apReduction = 0;
            bool ignoresPositiveDefense = false;
            if (completeOps.Count > 0)
            {
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

                // #431 Pierce: the stage's own fold, so an ignored Defense bonus is ignored here too.
                saveNet = RollToHitStage<ICombatMetadata>.WholeAttackSaveModifier(completeOps,
                    out ignoresPositiveDefense);
                foreach (RuleOperation op in completeOps)
                {
                    if (op is RuleOperation.ReduceArmorPenetration reduce) apReduction += reduce.Amount;
                    // #442 (#434 Hazardous): RollToHitStage carries these to ApplyWoundsStage, which lands each die's
                    // wounds on one firing model with the excess lost - so a die is worth at most one model's wounds.
                    else if (tally != null && op is RuleOperation.InflictSelfWounds self && self.Wounds > 0f)
                    {
                        float perDie = Math.Max(1f, self.WoundsPerDie);
                        tally.SelfWounds += self.Wounds / perDie * Math.Min(perDie, MaxModelWounds(atk));
                    }
                }
            }

            global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.VolleyComplete, __completeProbe);

            // --- CoverCheckStage stand-in: cover is a caller-supplied fact about the hypothetical
            // position; the ignore rules (Blast/Indirect/Takedown) are still the engine's own query.
            int coverBonus = !context.IsMelee && context.DefenderInCover && !ignoresPositiveDefense
                && !SightRuleQueries.IgnoresCover(atk, weapon, evaluator, context.AttackerMoved) ? 1 : 0;
            global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.VolleyHit, __hitProbe);

            var bonusAttacks = new BonusAttackSink();
            var __saveProbe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            float wounds = ResolveSaves(evaluator, atk, def, weapon, groups, apReduction,
                saveNet, coverBonus, context.IsMelee, notes, bonusAttacks, defenderModels, carrier,
                context.DistanceInches, chargeOrigin, tally: tally, ignoresPositiveDefense: ignoresPositiveDefense);
            global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.VolleySave, __saveProbe);

            // --- ResolveBonusMeleeAttacksStage mirror (#376 Bloodthirsty): the follow-up batch earned
            // by block-roll 1s, priced first-order - the same hit threshold and weapon, plain saves.
            // The engine's real child batch reruns the weapon's on-6 rules on the bonus dice and this
            // mirror does not (nor does it chain, matching the rule); exact for plain weapons,
            // understating exotic ones - the conservative side.
            if (bonusAttacks.TotalBonusAttacks > 0f)
            {
                IDiceResults bonusRolls = Dice.Roll(bonusAttacks.TotalBonusAttacks);
                float bonusHits = bonusRolls.AtOrAbove(DiceUtilities.ClampSuccessRollNeeded(hitRollNeeded));
                if (bonusHits > 0f)
                {
                    NoteIf(true, $"{weapon.Name}: Bloodthirsty follow-up priced first-order", notes);
                    wounds += ResolveSaves(evaluator, atk, def, weapon,
                        new List<SuccessfulHitInfo> { SingleGroupInfo(bonusHits) }, apReduction,
                        saveNet, coverBonus, context.IsMelee, notes, defenderModels: defenderModels,
                        carrier: carrier, distanceInches: context.DistanceInches, chargeOriginInches: chargeOrigin,
                        tally: tally, ignoresPositiveDefense: ignoresPositiveDefense);
                }
            }
            return wounds;
        }

        // --- DetermineSaveRollsNeeded + RollToSave + AssignWounds mirror (shared by volleys and
        // impact hits). Returns the expected wounds that LAND - the packet queue placed along the
        // allocation order, so a Deadly clump's excess and overkill are already gone (#401).
        private static float ResolveSaves(RuleEvaluator evaluator, UnitData atk, UnitData def,
            Weapon weapon, List<SuccessfulHitInfo> groups, int apReduction, int wholeAttackSaveModifier,
            int coverBonus, bool isMelee, List<string> notes, BonusAttackSink? bonusAttackSink = null,
            IReadOnlyList<IModel>? defenderModels = null, Weapon? carrier = null,
            float distanceInches = 0f, float chargeOriginInches = 0f, bool isSpell = false, Tally? tally = null,
            bool ignoresPositiveDefense = false, float preFailedWounds = 0f)
        {
            int baseDefense = HeroStatRules.GetSaveDefense(def);
            int ap = Math.Max(0, weapon.ArmorPenetration - apReduction);
            // #442: DetermineSaveRollsNeededStage's token folds, peeked - the defender's granted "+X to defense"
            // (#033; Pierce drops the positive ones) and the AP-side attacker-bonus markers (TargetMarkerSpend). Every
            // save pipeline runs that stage - volleys, impact hits and spell damage alike - so they apply to all three.
            int grantedDefense = GrantedRollModifiers.PeekNet(def, ERollKind.Save, ignorePositive: ignoresPositiveDefense);
            int baseDefenseWithAP = baseDefense + ap - coverBonus - wholeAttackSaveModifier
                - grantedDefense + MarkerNet(def, ERollKind.Save);

            float totalWounds = 0f;
            float[] combinedSaveFaces = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            var savedGroups = new List<(IDiceResults Saved, int SaveNeeded)>();

            // #442 (#434 Ravage): an auto-wound pool skips the save stages - SyntheticWoundResolution.AsUnsavedWounds
            // hands AssignWoundsStage the wound dice (top face) as PRE-FAILED saves, so only the wound-side rules apply.
            if (preFailedWounds > 0f)
            {
                totalWounds += preFailedWounds;
                combinedSaveFaces[^1] += preFailedWounds;
            }

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
            // confinement -> Shred injection -> Regeneration ignore. No terrain here (IHasTerrain):
            // valuation is position-hypothetical, so terrain-gated save rules (Grounded Protection)
            // conservatively don't fire - the bonus is only ever omitted, never wrongly granted.
            // #442 (#432's save-side arms): the distance and the charge's launch distance ride the save context as in
            // AssignWoundsStage, so Infected / Violent / Warbound's extra wound and Knightborn / Warding's 6+ ignore
            // price on the right side of 9"/6"; a spell's wounds carry IsSpell.
            var saveContext = new SaveRollCompleteContext(atk, def, new DiceResults(combinedSaveFaces), isMelee,
                isSpell, distanceInches, chargeOriginInches);
            RuleParticipant saveActor = RuleParticipant.Actor(atk, weapon);
            RuleParticipant saveSubject = RuleParticipant.Subject(def,
                models: defenderModels ?? HeroStatRules.LivingModels(def));
            IReadOnlyList<RuleOperation> saveCompleteOps = Ops(carrier == null
                ? evaluator.EvaluateAllNamed(saveContext, saveActor, saveSubject)
                : evaluator.EvaluateAllNamed(saveContext, saveActor, saveSubject, RuleParticipant.Actor(atk, carrier)));

            // #376 Bloodthirsty: collect the earned follow-up attacks for the caller's second pass.
            // Null on the second pass itself (and on the impact-hit path) - no chaining, and impact
            // hits are not weapon swings, matching the engine's consumption sites.
            // #191 search perf pass 5: every sink below reads as its default off an empty list.
            int? rerollSavesAtOrAbove = null;
            float extraWounds = 0f;
            bool hasIgnore = false;
            int ignoreThreshold = 0;
            if (saveCompleteOps.Count > 0)
            {
                bonusAttackSink?.ApplyFrom(saveCompleteOps);

                // #442 (#431 Scourge): AssignWoundsStage banks these on the defender; nothing is dealt.
                if (tally != null)
                    foreach (RuleOperation op in saveCompleteOps)
                        if (op is RuleOperation.AddCountedWoundsTaken counted) tally.Counted += counted.Count;

                var reroll = new RerollSink();
                reroll.ApplyFrom(saveCompleteOps);
                rerollSavesAtOrAbove = reroll.RerollSavesAtOrAbove;

                var woundInjection = new WoundInjectionSink();
                woundInjection.ApplyFrom(saveCompleteOps);
                extraWounds = woundInjection.TotalExtraWounds;

                var woundIgnore = new WoundIgnoreSink();
                woundIgnore.ApplyFrom(saveCompleteOps);
                hasIgnore = woundIgnore.HasIgnore;
                if (hasIgnore) ignoreThreshold = woundIgnore.Threshold;
            }

            // Mirrors AssignWoundsStage: the reroll threshold is the unmodified max (6) unless a Boost
            // variant widened it to 5-6, and the same clamp keeps an out-of-range authoring honest.
            if (rerollSavesAtOrAbove is int rerollFrom)
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
            int multiplier = 1;
            if (woundOps.Count > 0)
            {
                var woundMultiplier = new WoundModifierSink();
                woundMultiplier.ApplyFrom(woundOps);
                multiplier = woundMultiplier.NetMultiplier;
            }

            // #401: the very queue the stage builds - Deadly clumps, then Shred's pool - with
            // Regeneration rolled per packet and the result landed along the same allocation order.
            // One shared model, so the estimate cannot drift from the resolution.
            List<WoundPacket> packets = WoundAllocation.Packets(totalWounds, multiplier);
            if (extraWounds > 0f) packets.Add(WoundPacket.Unconfined(extraWounds));
            if (hasIgnore)
            {
                for (int i = 0; i < packets.Count; i++)
                {
                    float ignored = Dice.Roll(packets[i].Wounds).AtOrAbove(ignoreThreshold);
                    packets[i] = packets[i].WithWounds(packets[i].Wounds - ignored);
                }
            }

            // Takedown re-scopes the attack to one chosen model (BuildTargetListStage); estimate the
            // best case: confined to the healthiest living model, overkill lost.
            if (HasTargetIndividualModel(weapon))
            {
                IModel? healthiest = def.Models.Where(model => model.GetIsAlive())
                    .OrderByDescending(model => model.TotalWounds - model.WoundsDealt).FirstOrDefault();
                NoteIf(true, $"{weapon.Name}: Takedown priced against the healthiest single model", notes);
                return healthiest == null ? 0f : WoundAllocation.Simulate(packets, new[] { healthiest }, out _);
            }

            return WoundAllocation.Simulate(packets,
                WoundAllocation.Order(defenderModels ?? def.Models, def.JoinedHeroModelId), out _);
        }

        // --- Allocation mirror: drains the pool along WoundAllocation.Order (already-wounded first,
        // then whole models in unit order, joined hero last). Returns expected kills.
        private static float ExpectedKills(UnitData defender, float wounds, out bool destroysUnit)
        {
            destroysUnit = wounds >= defender.RemainingWounds && defender.RemainingWounds > 0f;

            float kills = 0f;
            float pool = wounds;
            foreach (IModel model in WoundAllocation.Order(defender))
            {
                if (pool <= 0f) break;
                float remaining = model.TotalWounds - model.WoundsDealt;
                if (remaining <= 0f) continue;
                if (pool >= remaining) { kills += 1f; pool -= remaining; }
                else break; // a partially wounded model still stands (and still fights)
            }
            return kills;
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
            foreach (IModel model in WoundAllocation.Order(unit))
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

        private static readonly WeaponComparer BatchComparer = new WeaponComparer();

        private static List<(Weapon Weapon, int Count)> Batch(List<Weapon> weapons)
        {
            var batches = new List<(Weapon Weapon, int Count)>(weapons.Count);
            foreach (Weapon weapon in weapons)
            {
                int index = -1;
                for (int i = 0; i < batches.Count && index < 0; i++)
                {
                    if (BatchComparer.Equals(batches[i].Weapon, weapon)) index = i;
                }
                if (index >= 0) batches[index] = (batches[index].Weapon, batches[index].Count + 1);
                else batches.Add((weapon, 1));
            }
            return batches;
        }

        private static float SwingAllBatches(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            List<(Weapon Weapon, int Count)> batches, AttackContext context, List<string> notes,
            Weapon? carrier = null, Tally? tally = null)
        {
            float total = 0f;
            foreach ((Weapon weapon, int count) in batches)
                total += EstimateVolleyCore(evaluator, attacker, defender, weapon, count, context, notes, carrier,
                    tally);
            return total;
        }

        // #442 (#434): the striker's auto-wound pools (InvokeDealAutoWounds) from a charge-contact evaluation, rolled
        // on the probabilistic roller and landed through wound assignment with pre-failed saves (ResolveRavageWounds-
        // Stage's child pipeline: a bare AP-0 "Ravage" weapon, so only the target's wound-side rules fold). 0 when the
        // striker already rolled its once-per-round strike dice.
        private static float AutoWounds(RuleEvaluator evaluator, UnitData striker, UnitData target,
            IReadOnlyList<RuleOperation> contactOps, List<string> notes, Tally tally)
        {
            if (striker.Tokens.HasToken(TokenType.StrikeDiceUsed)) return 0f;
            float expected = 0f;
            foreach (RuleOperation op in contactOps)
            {
                if (op is RuleOperation.InvokeDealAutoWounds { DiceCount: > 0 } pool)
                    expected += Dice.Roll(pool.DiceCount)
                        .AtOrAbove(DiceUtilities.ClampSuccessRollNeeded(pool.SuccessThreshold));
            }
            if (expected <= 0f) return 0f;
            return ResolveSaves(evaluator, striker, target,
                new Weapon("Ravage", rangeInches: 0f, attacks: 0, armorPenetration: 0),
                new List<SuccessfulHitInfo>(), apReduction: 0, wholeAttackSaveModifier: 0, coverBonus: 0,
                isMelee: true, notes, tally: tally, preFailedWounds: expected);
        }

        // The striking-back defender in the charger's seat (ResolveImpactHitsStage.ChargerParticipant) facing the
        // charger as the subject - StrikeBackStage's reversed Ravage evaluation.
        private static RuleParticipant[] DefenderStrikerParticipants(UnitData striker, UnitData target)
        {
            RuleParticipant[] subject = DetermineStrikeOrderStage.SubjectWithMeleeWeapons(target);
            var participants = new RuleParticipant[subject.Length + 1];
            participants[0] = ResolveImpactHitsStage.ChargerParticipant(striker);
            Array.Copy(subject, 0, participants, 1, subject.Length);
            return participants;
        }

        /// <summary>
        /// #442: a rule-only stand-in weapon carrying <paramref name="rule"/>, added to an evaluation as an extra Actor
        /// participant of the SAME attacking unit. The evaluator's per-event dedup is keyed on (bearer, rule), so the
        /// unit's own rules still fire once and only the hypothetical rule is new - the read-only way to ask "what if
        /// this unit also had rule X" (a pick it is about to make) without granting anything. Its ops fold through the
        /// very sinks the real grant's would (pinned against the stages in CombatMathPinTests). Null for null.
        /// </summary>
        private static Weapon? HypotheticalCarrier(ResolvedRule? rule)
        {
            if (rule == null) return null;
            var carrier = new Weapon("(hypothetical)", rangeInches: 0f, attacks: 0, armorPenetration: 0);
            carrier.AttachRuleDefinition(rule);
            return carrier;
        }

        private static float SumFear(RuleEvaluator evaluator, MeleeResolutionContext resolution, UnitData actor)
        {
            IReadOnlyList<RuleOperation> ops = Ops(evaluator.EvaluateAllNamed(resolution, RuleParticipant.Actor(actor)));
            float total = 0f;
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i] is RuleOperation.ExtraMeleeWoundCount extra) total += extra.Amount;
            }
            return total;
        }

        /// <summary>
        /// Expected whole-model kills from this many wounds into the defender, via the engine's
        /// own allocation order (#191 A5-4 - mob-break pricing needs kills, not just wounds).
        /// </summary>
        public static float ExpectedKillsFrom(UnitData defender, float wounds)
            => ExpectedKills(defender, Math.Min(wounds, defender.RemainingWounds), out _);

        private static AttackEstimate Finish(float totalWounds, DataBinding<UnitData> defender,
            IReadOnlyList<string> notes, float countedWounds = 0f, float selfWounds = 0f)
        {
            float remaining = defender.GetValue().RemainingWounds;
            float capped = Math.Min(totalWounds, remaining);
            float kills = ExpectedKills(defender.GetValue(), capped, out bool destroys);
            return new AttackEstimate(capped, totalWounds, kills, destroys, notes, countedWounds, selfWounds);
        }

        // #442: TargetMarkerSpend.ConsumeNet's total for one attack, as the AI answers its prompts (spend every Spotter
        // marker, remove one Tag token): persistent + spendable + one single-spend. Read-only. Approximation: the engine
        // spends spendable markers on the FIRST volley that rolls, this gives them to each volley of a multi-weapon
        // attack (the Tactician places no markers itself; they reach it only from an opponent's Spotters).
        private static int MarkerNet(UnitData defender, ERollKind roll)
        {
            (TokenType persistent, TokenType spendable, TokenType singleSpend) = roll == ERollKind.Hit
                ? (TokenType.PersistentHitBonus, TokenType.SpendableHitBonus, TokenType.SingleSpendHitBonus)
                : (TokenType.PersistentApBonus, TokenType.SpendableApBonus, TokenType.SingleSpendApBonus);
            return defender.Tokens.GetTokenCount(persistent) + defender.Tokens.GetTokenCount(spendable)
                + (defender.Tokens.GetTokenCount(singleSpend) > 0 ? 1 : 0);
        }

        // The most wounds any living model of the unit has left - the most one Hazardous die can take off it.
        private static float MaxModelWounds(UnitData unit)
        {
            float best = 1f;
            foreach (IModel model in unit.Models)
                if (model.GetIsAlive()) best = Math.Max(best, model.TotalWounds - model.WoundsDealt);
            return best;
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
            IReadOnlyList<(RuleOperation Op, string RuleName)> named)
        {
            // #191 search perf pass 5: the empty answer is by far the common one - no list for it.
            if (named.Count == 0) return Array.Empty<RuleOperation>();
            var ops = new List<RuleOperation>(named.Count);
            for (int i = 0; i < named.Count; i++) ops.Add(named[i].Op);
            return ops;
        }

        private static void NoteIf(bool condition, string note, List<string> notes)
        {
            if (condition && !notes.Contains(note)) notes.Add(note);
        }
    }
}
