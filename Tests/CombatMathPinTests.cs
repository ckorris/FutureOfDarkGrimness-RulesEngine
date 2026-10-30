using FDG.Ai.Tactician;
using FDG.SaveLoad;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A1 / plan sec. 6.3 — the CombatMath pin: the Tactician's closed-form expected-wounds
    // estimate must match what the REAL combat stages produce for the same volley under the
    // probabilistic roller. Every case runs both:
    //   engine   = DetermineHitRoll -> RollToHit -> DetermineSaveRollsNeeded -> RollToSave ->
    //              AssignWounds, driven directly (the *RuleIntegrationTests harness pattern),
    //   estimate = CombatMath.EstimateVolley with the same units, weapon, and context,
    // and asserts |delta| <= max(0.05, 2%) (the plan's tolerance; in practice the mirror is exact
    // because CombatMath runs the engine's own rule evaluation and dice histograms).
    //
    // The engine is ground truth (plan G1): when a case fails, fix CombatMath, never the tolerance.
    [TestFixture]
    public class CombatMathPinTests
    {
        private GameDataStore _store = null!;
        private CapturingWoundRequester _requester = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new CapturingWoundRequester();
            _ctx = new WoundTestContext(_store, _requester, new ProbabilisticDiceRoller());
        }

        // --- Baseline math: quality x defense sweep, no rules -----------------------------------

        [Test]
        public async Task Baseline_QualityByDefenseSweep_MatchesEngine([Range(2, 6)] int quality,
            [Range(2, 6)] int defense)
        {
            var weapon = Rifle();
            var attacker = MakeUnit(3, weapon, quality: quality);
            var defender = MakeUnit(5, null, defense: defense);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public async Task ArmorPenetration_MatchesEngine(int ap)
        {
            var weapon = Rifle(ap: ap);
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        [Test]
        public async Task MultiAttackWeapons_MatchEngine()
        {
            var weapon = Rifle(attacks: 3);
            var attacker = MakeUnit(5, weapon);
            var defender = MakeUnit(5, null);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 5);
        }

        [Test]
        public async Task Cover_MatchesEngine()
        {
            var weapon = Rifle(ap: 1);
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3, defenderInCover: true);
        }

        // --- Hit-side rules ----------------------------------------------------------------------

        [Test]
        public async Task Reliable_QualityFloor_MatchesEngine()
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Reliable", CoreRuleCatalog.Reliable));
            var attacker = MakeUnit(3, weapon, quality: 5);
            var defender = MakeUnit(5, null);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // Stealth is distance-gated (> 9"): both sides of the gate must pin.
        [TestCase(30f)]
        [TestCase(4f)]
        public async Task Stealth_DistanceGated_MatchesEngine(float defenderX)
        {
            var weapon = Rifle();
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null, atX: defenderX);
            AttachUnitRule(defender, "Stealth", CoreRuleCatalog.Stealth);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        [Test]
        public async Task ShieldedDefender_MatchesEngine()
        {
            var weapon = Rifle(ap: 1);
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);
            AttachUnitRule(defender, "Shielded", CoreRuleCatalog.Shielded);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // Fortified reduces weapon AP floored at 0 - both against real AP and against AP 0.
        [TestCase(2)]
        [TestCase(0)]
        public async Task FortifiedDefender_MatchesEngine(int weaponAp)
        {
            var weapon = Rifle(ap: weaponAp);
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);
            AttachUnitRule(defender, "Fortified", CoreRuleCatalog.Fortified);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // --- #427 Unstoppable: "ignores all negative modifiers to this weapon which are not from this model's
        // special rules". The estimate runs the engine's own rule evaluation, so the evaluator-level immunity
        // reaches the AI and the calculator with no CombatMath change - these pin that it does.
        [Test]
        public async Task UnstoppableWeapon_AgainstStealth_MatchesEngine_AndOutdamagesAPlainOne()
        {
            var unstoppable = Rifle();
            unstoppable.AttachRuleDefinition(new ResolvedRule("Unstoppable", CoreRuleCatalog.Unstoppable));
            var attacker = MakeUnit(3, unstoppable);
            var defender = MakeUnit(5, null, atX: 40f);
            AttachUnitRule(defender, "Stealth", CoreRuleCatalog.Stealth);

            await AssertVolleyPinned(attacker, defender, unstoppable, weaponCount: 3);

            var plain = Rifle();
            var plainAttacker = MakeUnit(3, plain);
            AttackContext far = new AttackContext(Distance(plainAttacker, defender));
            float withStealth = CombatMath.EstimateVolley(_ctx.RuleEvaluator, plainAttacker, defender, plain, 3, far,
                new List<string>());
            float ignoring = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, unstoppable, 3, far,
                new List<string>());
            Assert.That(ignoring, Is.GreaterThan(withStealth + 0.1f), "the estimate prices Stealth away");
        }

        [Test]
        public async Task UnstoppableWeapon_AgainstFortified_MatchesEngine()
        {
            var weapon = Rifle(ap: 2);
            weapon.AttachRuleDefinition(new ResolvedRule("Unstoppable", CoreRuleCatalog.Unstoppable));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);
            AttachUnitRule(defender, "Fortified", CoreRuleCatalog.Fortified);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        [Test]
        public async Task UnstoppableWeapon_KeepsItsOwnPenalty_MatchesEngine()
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Unstoppable", CoreRuleCatalog.Unstoppable));
            weapon.AttachRuleDefinition(new ResolvedRule("Imprecise", WeaponRules36Definitions.Imprecise));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null, atX: 40f);
            AttachUnitRule(defender, "Stealth", CoreRuleCatalog.Stealth);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // --- Natural-6 / per-hit AP rules ---------------------------------------------------------

        [Test]
        public async Task Rending_PerHitApAndRegenIgnore_MatchesEngine()
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);
            AttachUnitRule(defender, "Regeneration", CoreRuleCatalog.Regeneration);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // --- Save-side rules ------------------------------------------------------------------------

        [Test]
        public async Task Regeneration_MatchesEngine()
        {
            var weapon = Rifle(ap: 2);
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);
            AttachUnitRule(defender, "Regeneration", CoreRuleCatalog.Regeneration);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        [Test]
        public async Task Unstoppable_IgnoresRegeneration_MatchesEngine()
        {
            var weapon = Rifle(ap: 2);
            weapon.AttachRuleDefinition(new ResolvedRule("Unstoppable", CoreRuleCatalog.Unstoppable));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);
            AttachUnitRule(defender, "Regeneration", CoreRuleCatalog.Regeneration);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        [TestCase("Bane")]
        [TestCase("Lacerate")]
        public async Task SaveRerollRules_MatchEngine(string ruleName)
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule(ruleName,
                ruleName == "Bane" ? CoreRuleCatalog.Bane : CoreRuleCatalog.Lacerate));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        [Test]
        public async Task Shred_ExtraWoundPerSavedOne_MatchesEngine()
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Shred", CoreRuleCatalog.Shred));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // --- Extra-hit / multiplier rules -----------------------------------------------------------

        // #376 Bloodthirsty: the follow-up batch earned by block-roll 1s. The engine runs it as a real
        // child batch after the base swing (ResolveBonusMeleeAttacksStage); the estimate prices it
        // first-order inside EstimateVolley. Under the probabilistic roller both are pure expectations,
        // so for a plain weapon they must agree. Engine total is measured on the defender's ledger,
        // since the bonus batch's wounds land in the child chain's own assign/apply pass.
        [Test]
        public async Task Bloodthirsty_FollowUpAttacks_MatchEngine()
        {
            var weapon = Blade(attacks: 6);
            var attacker = MakeUnit(1, weapon);
            AttachUnitRule(attacker, "Bloodthirsty Probe", new SpecialRuleDefinition("Bloodthirsty Probe",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.IsMelee(),
                        new Effect.AddBonusAttack(OnRollValue: 1, Count: 1), ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>()));
            var defender = MakeUnit(10, null);
            float start = defender.RemainingWounds();

            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: true);
            metadata.AddResult(new CoverCheckResults(0));
            await RunStage(new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToSaveStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new ApplyWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            var bonusStage = new ResolveBonusMeleeAttacksStage(_ctx, new NoOpLayer<ICombatMetadata>());
            bonusStage.OnBonusAttacksResolved.Bind("done");
            await bonusStage.Enter(metadata);
            float engineWounds = start - defender.RemainingWounds();

            var notes = new List<string>();
            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender,
                weapon, weaponCount: 1, new AttackContext(1f, IsMelee: true), notes);

            float tolerance = Math.Max(0.05f, 0.02f * engineWounds);
            Assert.That(estimate, Is.EqualTo(engineWounds).Within(tolerance),
                $"the follow-up batch must be priced (notes: {string.Join("; ", notes)})");
        }

        [Test]
        public async Task Surge_ExtraHitOnSix_MatchesEngine()
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Surge", CoreRuleCatalog.Surge));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        [TestCase(30f)]
        [TestCase(4f)]
        public async Task Relentless_DistanceGated_MatchesEngine(float defenderX)
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Relentless", CoreRuleCatalog.Relentless));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null, atX: defenderX);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // Blast multiplies EACH hit, capped per hit at the target's living model count and stacking
        // across hits: a big unit absorbs the full multiplier, a small one clips it.
        [TestCase(10)]
        [TestCase(2)]
        public async Task Blast_CappedAtModelCount_MatchesEngine(int defenderModels)
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Blast", CoreRuleCatalog.Blast,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(defenderModels, null);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // Deadly's clump confinement differs sharply between 1-wound and Tough targets - both pin.
        [TestCase(1)]
        [TestCase(3)]
        public async Task Deadly_ClumpConfinement_MatchesEngine(int woundsPerModel)
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Deadly", CoreRuleCatalog.Deadly,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(4, null, woundsPerModel: woundsPerModel);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 3);
        }

        // --- Melee volleys (same shared stages, isMelee/isCharging flags) ---------------------------

        [Test]
        public async Task MeleeSwing_Basic_MatchesEngine()
        {
            var blade = Blade();
            var attacker = MakeUnit(3, blade, atX: 0f);
            var defender = MakeUnit(3, null, atX: 1f);

            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3, isMelee: true);
        }

        [Test]
        public async Task Furious_ChargeGated_MatchesEngine()
        {
            var blade = Blade();
            var attacker = MakeUnit(3, blade, atX: 0f);
            AttachUnitRule(attacker, "Furious", CoreRuleCatalog.Furious);
            var defender = MakeUnit(3, null, atX: 1f);

            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3,
                isMelee: true, isCharging: true);
            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3,
                isMelee: true, isCharging: false); // gate closed: no extra hits
        }

        // #433: Enhanced Bloodborn's extra hit on a SUCCESSFUL natural 5 reads the hit threshold the AI threads onto its
        // mirrored hit context - at 4+ the 5s pay out, under Fatigue (6s only, and they don't count) nothing does.
        [Test]
        public async Task EnhancedBloodborn_SuccessfulFives_MatchesEngine()
        {
            var blade = Blade();
            var attacker = MakeUnit(3, blade, atX: 0f);
            AttachUnitRule(attacker, "Bloodborn", new SpecialRuleDefinition("Bloodborn",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.UnmodifiedRollEquals(6),
                        new Effect.AddExtraHit(6, 1), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>()));
            AttachUnitRule(attacker, "Enhanced Bloodborn", Enhanced36Definitions.HitsOnFive());
            var defender = MakeUnit(3, null, atX: 1f);

            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3, isMelee: true, isCharging: true);

            attacker.GetValue().Tokens.AddToken(new Token(TokenType.Fatigued, 1, new TokenClearTrigger.RoundEnd()));
            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3, isMelee: true, isCharging: true);
        }

        // #423 Parry(X): the defender's hit-ignore roll between the hits and the saves. The estimate folds the stage's own
        // hits-taken operation (ParryBudgetFor) and removal order (RemoveWorstHitsFirst) - never the name "Parry".
        [Test]
        public async Task Parry_OnTheDefender_MatchesEngine()
        {
            // 3 blades, 6 attacks at 4+ -> 3 hits; 5 x Parry(1) capped at 3 dice -> 0.5 hits ignored -> 2.5 hits, half
            // saved -> 1.25 wounds (1.5 without Parry).
            var blade = Blade();
            var attacker = MakeUnit(3, blade, atX: 0f);
            var defender = MakeUnit(5, HandWeapon(1), atX: 1f);

            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3, isMelee: true);
        }

        [Test]
        public async Task Parry_AgainstRending_RemovesTheWorstGroupFirst_MatchesEngine()
        {
            // 4 Rending blades, 8 attacks: 4 hits of which 4/3 are natural 6s (AP+4, unsaveable at D4). 2 x Parry(3) = 6
            // dice capped at 4 -> 2/3 of a hit ignored, all of it out of the Rending group.
            var blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            var attacker = MakeUnit(4, blade, atX: 0f);
            var defender = MakeUnit(2, HandWeapon(3), atX: 1f);

            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 4, isMelee: true);
        }

        [Test]
        public async Task Thrust_ChargeHitAndApBonus_MatchesEngine()
        {
            var blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule("Thrust", CoreRuleCatalog.Thrust));
            var attacker = MakeUnit(3, blade, atX: 0f);
            var defender = MakeUnit(3, null, atX: 1f);

            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3,
                isMelee: true, isCharging: true);
        }

        [Test]
        public async Task FatiguedAttacker_HitsOnlyOnSixes_MatchesEngine()
        {
            var blade = Blade();
            var attacker = MakeUnit(3, blade, atX: 0f);
            attacker.GetValue().Tokens.AddToken(new Token(TokenType.Fatigued, 1,
                new TokenClearTrigger.RoundEnd()));
            var defender = MakeUnit(3, null, atX: 1f);

            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3, isMelee: true);
        }

        // #418: a fatigued charge's natural 6s don't count as 6s for special rules, so Furious (unit),
        // Surge and Rending (weapon) all price nothing - and the estimate must agree with the engine on it.
        // Hand value: 6 dice hitting only on 6s = 1 hit at base AP, saved on 4+ = 0.5 wounds. Without the
        // view the estimate would carry Furious's and Surge's extra hits and Rending's AP(+4) on the 6.
        [TestCase(true)]
        [TestCase(false)]
        public async Task FatiguedAttacker_SixTriggersPriceNothing_MatchesEngine(bool fatigued)
        {
            var blade = Blade();
            blade.AttachRuleDefinition(new ResolvedRule("Surge", CoreRuleCatalog.Surge));
            blade.AttachRuleDefinition(new ResolvedRule("Rending", CoreRuleCatalog.Rending));
            var attacker = MakeUnit(3, blade, atX: 0f);
            AttachUnitRule(attacker, "Furious", CoreRuleCatalog.Furious);
            if (fatigued)
                FatigueUtilities.ApplyFatigued(attacker.GetValue());
            var defender = MakeUnit(5, null, atX: 1f);

            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, blade, 3,
                new AttackContext(Distance(attacker, defender), IsMelee: true, IsCharging: true),
                new List<string>());
            if (fatigued)
                Assert.That(estimate, Is.EqualTo(0.5f).Within(0.0001f), "one base-AP hit, half saved");

            await AssertVolleyPinned(attacker, defender, blade, weaponCount: 3, isMelee: true, isCharging: true);
        }

        // --- Unit-level estimates (composition; the volley math above is the pinned core) -----------

        [Test]
        public void EstimateShooting_OutOfRangeWeaponContributesNothing()
        {
            var weapon = Rifle(range: 12f);
            var attacker = MakeUnit(3, weapon, atX: 0f);
            var defender = MakeUnit(3, null, atX: 30f);

            AttackEstimate estimate = CombatMath.EstimateShooting(_ctx.RuleEvaluator, attacker, defender,
                new AttackContext(DistanceInches: Distance(attacker, defender)));

            Assert.That(estimate.ExpectedWounds, Is.EqualTo(0f));
        }

        [Test]
        public void EstimateMelee_ImpactHits_AddExpectedWounds()
        {
            var blade = Blade();
            var attacker = MakeUnit(2, blade, atX: 0f);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(4) }));
            var defender = MakeUnit(5, null, atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            // Impact(4) on 2 models (#447: per model): 8 dice at 2+ -> 8 * 5/6 hits; D4 save at AP0 -> half fail.
            // Swings: 2 blades x 2 attacks at Q4 -> 2 hits -> 1 wound. Total 4.333 + margin for exactness.
            float impactWounds = 8f * (5f / 6f) * 0.5f;
            float swingWounds = 2f * 2f * 0.5f * 0.5f;
            Assert.That(estimate.AttackerAttack.ExpectedWounds,
                Is.EqualTo(impactWounds + swingWounds).Within(0.001f));
        }

        [Test]
        public void EstimateMelee_CounterDefender_StrikesFirstAndStripsCharge()
        {
            var blade = Blade();
            var attacker = MakeUnit(3, blade, atX: 0f);
            AttachUnitRule(attacker, "Furious", CoreRuleCatalog.Furious);

            var counterBlade = Blade();
            counterBlade.AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));
            var defender = MakeUnit(3, counterBlade, atX: 1f);

            MeleeEstimate withCounter = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            var plainDefender = MakeUnit(3, Blade(), atX: 1f);
            MeleeEstimate without = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, plainDefender);

            Assert.That(withCounter.DefenderStrikesFirst, Is.True);
            Assert.That(without.DefenderStrikesFirst, Is.False);
            // Furious fires only while charging; Counter's swap strips the charge, so the charger's
            // output drops below its uncountered self even before casualties are considered.
            Assert.That(withCounter.AttackerAttack.ExpectedWounds,
                Is.LessThan(without.AttackerAttack.ExpectedWounds));
        }

        // #422 - the AI reads Impact, Brace and Counter through the same evaluation the stages run, so the 3.6
        // Fatigue clauses and the Counter/Brace split reach it with no hand-written mirror. Pinned anyway:
        // a regression here would mis-value every charge into (or by) a tired unit.

        [Test]
        public void EstimateMelee_FatiguedImpactCharger_AddsNoImpactWounds()
        {
            var attacker = MakeUnit(2, Blade(), atX: 0f);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(4) }));
            FatigueUtilities.ApplyFatigued(attacker.GetValue());
            var defender = MakeUnit(5, null, atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            // No impact dice; the fatigued swings hit on 6s only: 2 blades x 2 attacks x 1/6, half saved.
            float swingWounds = 2f * 2f * (1f / 6f) * 0.5f;
            Assert.That(estimate.AttackerAttack.ExpectedWounds, Is.EqualTo(swingWounds).Within(0.001f));
        }

        [Test]
        public void EstimateMelee_BraceDefender_CancelsImpactDice()
        {
            var attacker = MakeUnit(2, Blade(), atX: 0f);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(2) }));
            var spear = new Weapon("Spear", rangeInches: 0f, attacks: 0, armorPenetration: 0);
            spear.AttachRuleDefinition(new ResolvedRule("Brace", CoreRuleCatalog.Brace,
                new RuleArgument[] { new RuleArgument.Int(1) }));
            var defender = MakeUnit(3, spear, atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            // Impact(2) x 2 models (#447) - Brace(1) x 3 spearmen = 1 die at 2+, half saved; swings 2 x 2 at Q4,
            // half saved. (Brace subtracts from the per-model total.)
            float impactWounds = 1f * (5f / 6f) * 0.5f;
            float swingWounds = 2f * 2f * 0.5f * 0.5f;
            Assert.That(estimate.AttackerAttack.ExpectedWounds,
                Is.EqualTo(impactWounds + swingWounds).Within(0.001f));
        }

        // #447 - Impact(X) is per model, and the estimate runs the stage's own charge-contact participants
        // (ResolveImpactHitsStage.ChargerParticipant), so a joined hero's own Impact adds its dice too.
        [Test]
        public void EstimateMelee_ImpactDice_SumPerModel_AJoinedHerosImpactAdds()
        {
            var attacker = MakeUnit(2, Blade(), atX: 0f);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(1) }));
            var hero = MakeUnit(1, null, atX: 0f);
            hero.GetValue().AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, attacker.GetValue()),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero.GetValue()),
            });
            var defender = MakeUnit(10, null, atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            // 2 x Impact(1) + the hero's Impact(3) = 5 dice at 2+, half saved; swings 2 x 2 at Q4, half saved.
            float impactWounds = 5f * (5f / 6f) * 0.5f;
            float swingWounds = 2f * 2f * 0.5f * 0.5f;
            Assert.That(estimate.AttackerAttack.ExpectedWounds,
                Is.EqualTo(impactWounds + swingWounds).Within(0.001f));
        }

        // #423: the Parry reduction with the integration test's numbers - 2 hits, 3 carriers, 2 dice, 1/3 of a hit ignored.
        [Test]
        public void EstimateMelee_ParryDefender_ReducesTheChargersWounds()
        {
            var attacker = MakeUnit(2, Blade(), atX: 0f);
            var defender = MakeUnit(3, HandWeapon(1), atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            // 2 blades x A2 = 4 attacks at 4+ -> 2 hits; 3 x Parry(1) = 3 dice capped at 2 -> 2/6 ignored -> 5/3 hits,
            // half saved -> 5/6 wounds (1.0 without Parry).
            Assert.That(estimate.AttackerAttack.ExpectedWounds, Is.EqualTo(5f / 6f).Within(0.001f));
        }

        // #423 B1: one pool per attack. 3 x Parry(1) against a charger swinging two profiles of 2 expected hits each:
        // the claws get 2 dice (1/3 ignored), the fangs the 1 left (1/6 ignored) - not a fresh 2 (B2 would price 1/3 again).
        [Test]
        public void EstimateMelee_TwoProfileCharger_SharesOneParryPoolAcrossItsSwings()
        {
            var claws = new Weapon("Claws", rangeInches: 0f, attacks: 4, armorPenetration: 0);
            var fangs = new Weapon("Fangs", rangeInches: 0f, attacks: 4, armorPenetration: 0);
            var beast = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon> { claws, fangs },
                initialPosition: new Position(0f, 0f), gameDataStore: _store);
            beast.SetMaxWounds(3);
            var attacker = _store.GetDataBinding<UnitData>(_store.Create(new UnitData(new PlayerID(Guid.NewGuid()),
                "Beast", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(beast)) })));
            var defender = MakeUnit(3, HandWeapon(1), atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            // claws: 2 hits - 1/3 = 5/3 -> 5/6 wounds; fangs: 2 hits - 1/6 = 11/6 -> 11/12 wounds; total 21/12.
            Assert.That(estimate.AttackerAttack.ExpectedWounds, Is.EqualTo(21f / 12f).Within(0.001f));
        }

        // #423: the strike-back is a new attack - the CHARGER's own Parry pool prices the return strikes.
        [Test]
        public void EstimateMelee_TheStrikeBack_IsParriedByTheChargersOwnPool()
        {
            var attacker = MakeUnit(2, HandWeapon(1), atX: 0f, woundsPerModel: 3);
            var defender = MakeUnit(4, Blade(), atX: 1f, woundsPerModel: 3);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            // Charger: 2 hand weapons x A1 -> 1 hit, no Parry on the defender -> 0.5 wounds. Return: 4 blades x A2 = 8
            // attacks -> 4 hits; the charger's 2 x Parry(1) = 2 dice -> 1/3 ignored -> 11/3 hits -> 11/6 wounds.
            Assert.That(estimate.AttackerAttack.ExpectedWounds, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(estimate.DefenderReturn.ExpectedWounds, Is.EqualTo(11f / 6f).Within(0.001f));
        }

        [Test]
        public void EstimateMelee_FatiguedCounterDefender_DoesNotStrikeFirst()
        {
            var attacker = MakeUnit(3, Blade(), atX: 0f);
            var counterBlade = Blade();
            counterBlade.AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));
            var defender = MakeUnit(3, counterBlade, atX: 1f);
            FatigueUtilities.ApplyFatigued(defender.GetValue());

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            Assert.That(estimate.DefenderStrikesFirst, Is.False,
                "a Fatigued Counter unit keeps the normal order, in the AI's estimate as in the stage.");
        }

        [Test]
        public void EstimateMelee_Fear_CountsTowardResolutionOnly()
        {
            var blade = Blade();
            var attacker = MakeUnit(3, blade, atX: 0f);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Fear", CoreRuleCatalog.Fear,
                new RuleArgument[] { new RuleArgument.Int(2) }));
            var defender = MakeUnit(3, null, atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            Assert.That(estimate.AttackerFearBonus, Is.EqualTo(2f));
            Assert.That(estimate.DefenderFearBonus, Is.EqualTo(0f));
        }

        [Test]
        public void EstimateMelee_ReturnStrikes_LoseKilledModels()
        {
            // Attacker: 4 blades, 8 attacks at Q4 -> 4 hits -> 2 wounds -> 2 expected kills.
            // Defender: 4 blades; return strikes should come from ~2 survivors, not 4.
            var blade = Blade();
            var attacker = MakeUnit(4, blade, atX: 0f);
            var defenderBlade = Blade();
            var defender = MakeUnit(4, defenderBlade, atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            float fullStrengthReturn = 4f * 2f * 0.5f * 0.5f;
            float survivorReturn = 2f * 2f * 0.5f * 0.5f;
            Assert.That(estimate.DefenderReturn.ExpectedWounds, Is.EqualTo(survivorReturn).Within(0.001f),
                $"return strikes must come from survivors only (full-strength would be {fullStrengthReturn}).");
        }

        // --- #442: a HYPOTHETICAL attacker rule (the activation-pick valuation) ---------------------------------
        // Versatile Warrior's helpers (the AP(+1) and +1-to-hit picks, range-conditional on "over 9"") priced as if
        // granted, on a twin that does NOT carry them, must equal the real stages run with the helper attached -
        // on both sides of the 9" gate.

        [TestCase(10f, true)]
        [TestCase(8f, true)]
        [TestCase(10f, false)]
        [TestCase(8f, false)]
        public async Task HypotheticalRule_PricesExactlyLikeTheGrantedRule(float gapInches, bool piercing)
        {
            SpecialRuleDefinition helper = piercing
                ? RangeConditional36Definitions.PickPiercing()
                : RangeConditional36Definitions.PickPrecision();
            var weapon = Rifle();
            var withRule = MakeUnit(3, weapon, quality: 4, atX: 0f);
            AttachUnitRule(withRule, helper.Name, helper);
            var twin = MakeUnit(3, weapon, quality: 4, atX: 0f);
            var defender = MakeUnit(5, null, defense: 3, atX: gapInches + 1f);
            float distance = Distance(twin, defender);

            float engine = await EngineVolleyWounds(withRule, defender, weapon, 3);
            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, twin, defender, weapon, 3,
                new AttackContext(distance), new List<string>(), new ResolvedRule(helper.Name, helper));
            float plain = CombatMath.EstimateVolley(_ctx.RuleEvaluator, twin, defender, weapon, 3,
                new AttackContext(distance), new List<string>());

            Assert.That(estimate, Is.EqualTo(engine).Within(Math.Max(0.05f, 0.02f * engine)));
            if (gapInches > 9f)
                Assert.That(estimate, Is.GreaterThan(plain + 0.01f), "over 9\" the pick must add value");
            else
                Assert.That(estimate, Is.EqualTo(plain).Within(0.0001f), "inside 9\" the pick adds nothing");
        }

        [Test]
        public void HypotheticalRule_InMelee_ReachesTheChargersSwingsOnly()
        {
            SpecialRuleDefinition helper = RangeConditional36Definitions.PickPrecision();
            var attacker = MakeUnit(3, Blade(), atX: 0f);
            var defender = MakeUnit(3, Blade(), atX: 1f);
            var rule = new ResolvedRule(helper.Name, helper);

            MeleeEstimate plain = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);
            MeleeEstimate picked = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender,
                hypotheticalAttackerRule: rule);

            // The Precision helper is range-conditional and a charge from base contact is not "over 6"", so it
            // must not fire here - and the return strikes never see the attacker's hypothetical rule at all.
            Assert.That(picked.AttackerAttack.ExpectedWounds, Is.EqualTo(plain.AttackerAttack.ExpectedWounds).Within(0.0001f));
            Assert.That(picked.DefenderReturn.ExpectedWounds, Is.EqualTo(plain.DefenderReturn.ExpectedWounds).Within(0.0001f));
        }

        // --- #442 (#432's charge arms and save-side arms) ---------------------------------------------------
        // The charge's launch distance reaches the charger's hit contexts and the save context, and the shooting
        // distance reaches the save context, exactly as CombatMetadata carries them through the real stages - so
        // every "shoots/charges (or is shot/charged) from over 9"/6"" arm prices on the right side of the line.

        [TestCase(7f, true)]
        [TestCase(5f, false)]
        public async Task Ferocious_ChargeArm_MatchesEngine(float origin, bool fires)
        {
            var blade = Blade(attacks: 3);
            var attacker = MakeUnit(3, blade, atX: 0f);
            AttachUnitRule(attacker, "Ferocious", RangeConditional36Definitions.ExtraHit("Ferocious"));
            var defender = MakeUnit(5, null, atX: 1f);
            float distance = Distance(attacker, defender);

            float engine = await EngineVolleyWounds(attacker, defender, blade, 3, isMelee: true, isCharging: true,
                chargeOriginInches: origin);
            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, blade, 3,
                new AttackContext(distance, IsMelee: true, IsCharging: true, ChargeOriginInches: origin),
                new List<string>());
            float noOrigin = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, blade, 3,
                new AttackContext(distance, IsMelee: true, IsCharging: true), new List<string>());

            Assert.That(estimate, Is.EqualTo(engine).Within(Math.Max(0.05f, 0.02f * engine)));
            if (fires) Assert.That(estimate, Is.GreaterThan(noOrigin + 0.01f), "a charge from over 6\" scores the extra hit");
            else Assert.That(estimate, Is.EqualTo(noOrigin).Within(0.0001f));
        }

        [TestCase(7f)]
        [TestCase(5f)]
        public async Task ChangeboundDefender_ChargedFromOverSix_MatchesEngine(float origin)
        {
            var blade = Blade(attacks: 3);
            var attacker = MakeUnit(3, blade, atX: 0f);
            var defender = MakeUnit(5, null, atX: 1f);
            AttachUnitRule(defender, "Changebound", RangeConditional36Definitions.MinusHit("Changebound"));
            float distance = Distance(attacker, defender);

            float engine = await EngineVolleyWounds(attacker, defender, blade, 3, isMelee: true, isCharging: true,
                chargeOriginInches: origin);
            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, blade, 3,
                new AttackContext(distance, IsMelee: true, IsCharging: true, ChargeOriginInches: origin),
                new List<string>());

            Assert.That(estimate, Is.EqualTo(engine).Within(Math.Max(0.05f, 0.02f * engine)));
        }

        [TestCase(10f)]
        [TestCase(8f)]
        public async Task KnightbornDefender_SaveSideShootingArm_MatchesEngine(float gapInches)
        {
            var weapon = Rifle(attacks: 2);
            var attacker = MakeUnit(5, weapon, atX: 0f);
            var defender = MakeUnit(5, null, defense: 5, atX: gapInches + 1f);
            AttachUnitRule(defender, "Knightborn", RangeConditional36Definitions.Ward("Knightborn"));
            float distance = Distance(attacker, defender);

            float engine = await EngineVolleyWounds(attacker, defender, weapon, 5);
            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, weapon, 5,
                new AttackContext(distance), new List<string>());

            Assert.That(estimate, Is.EqualTo(engine).Within(Math.Max(0.05f, 0.02f * engine)),
                "the 6+ ignore applies only when shot from over 9\"");
        }

        [TestCase(10f)]
        [TestCase(8f)]
        public async Task InfectedWeapon_SaveSideShootingArm_MatchesEngine(float gapInches)
        {
            var weapon = Rifle(attacks: 2);
            weapon.AttachRuleDefinition(new ResolvedRule("Infected", RangeConditional36Definitions.ExtraWound()));
            var attacker = MakeUnit(5, weapon, atX: 0f);
            var defender = MakeUnit(10, null, defense: 4, atX: gapInches + 1f);
            float distance = Distance(attacker, defender);

            float engine = await EngineVolleyWounds(attacker, defender, weapon, 5);
            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, weapon, 5,
                new AttackContext(distance), new List<string>());

            Assert.That(estimate, Is.EqualTo(engine).Within(Math.Max(0.05f, 0.02f * engine)),
                "1s to block deal an extra wound only from over 9\"");
        }

        [Test]
        public void EstimateMelee_ChargeOrigin_ReachesTheChargerOnly_AndACounterStripsIt()
        {
            var attacker = MakeUnit(3, Blade(attacks: 3), atX: 0f);
            AttachUnitRule(attacker, "Ferocious", RangeConditional36Definitions.ExtraHit("Ferocious"));
            var defender = MakeUnit(5, Blade(), atX: 1f);
            AttachUnitRule(defender, "Ferocious", RangeConditional36Definitions.ExtraHit("Ferocious"));
            var plainDefender = MakeUnit(5, Blade(), atX: 1f);

            MeleeEstimate far = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender, chargeOriginInches: 8f);
            MeleeEstimate near = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender, chargeOriginInches: 4f);
            MeleeEstimate farPlain = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, plainDefender,
                chargeOriginInches: 8f);
            Assert.That(far.AttackerAttack.ExpectedWounds, Is.GreaterThan(near.AttackerAttack.ExpectedWounds + 0.01f));
            Assert.That(far.DefenderReturn.ExpectedWounds,
                Is.EqualTo(farPlain.DefenderReturn.ExpectedWounds).Within(0.0001f),
                "a strike-back is never a charge: the defender's own Ferocious never fires");

            var counterBlade = Blade(attacks: 3);
            counterBlade.AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));
            var counter = MakeUnit(5, counterBlade, atX: 1f);
            MeleeEstimate countered = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, counter, chargeOriginInches: 8f);
            MeleeEstimate counteredNear = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, counter, chargeOriginInches: 4f);
            Assert.That(countered.AttackerAttack.ExpectedWounds,
                Is.EqualTo(counteredNear.AttackerAttack.ExpectedWounds).Within(0.0001f),
                "a Countered charge loses its charge, and with it the launch distance (CombatActionContext)");
        }

        [Test]
        public async Task EstimateSpellDamage_SpellArm_MatchesTheSpellStages()
        {
            var caster = MakeUnit(1, null, atX: 0f);
            var defender = MakeUnit(5, null, defense: 5, atX: 6f);
            AttachUnitRule(defender, "Knightborn", RangeConditional36Definitions.Ward("Knightborn"));
            var spell = new RuntimeSpell(new SpellDefinition("Bolt", 1,
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                new Effect.DealHits(6, Array.Empty<string>(), ArmorPenetration: 1)), Array.Empty<ResolvedRule>());

            // Engine side - ResolveSpellDamageStage's child chain, built the same way.
            var weapon = new Weapon("Bolt", rangeInches: 0f, attacks: 0, armorPenetration: 1);
            SyntheticHitResolution.Result hits = SyntheticHitResolution.Resolve(_ctx, caster.GetValue(),
                defender.GetValue(), 6, weapon, isSpell: true);
            var metadata = new CombatMetadata(_ctx, caster, defender, weapon, weaponCount: 1, isMelee: false,
                isSpell: true);
            var hitResults = new RollToHitResults(hits.HitGroups, new List<FailedHitInfo>())
            {
                SaveModifier = hits.SaveModifier,
                ArmorPenetrationReduction = hits.ArmorPenetrationReduction,
            };
            metadata.AddResult(hitResults);
            metadata.AddResult(new CoverCheckResults(0));
            await RunStage(new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToSaveStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            Assert.That(metadata.QueryForResult(out AssignWoundsResults engineResults), Is.True);

            AttackEstimate estimate = CombatMath.EstimateSpellDamage(_ctx.RuleEvaluator, caster, defender, spell);

            // 6 hits, Defense 5 at AP 1 saves on 6+ only -> 5 wounds, of which 5/6 are ignored on the spell arm's 2+.
            Assert.That(estimate.ExpectedWounds, Is.EqualTo(engineResults.TotalAssignedWounds).Within(0.05f));
            Assert.That(estimate.ExpectedWounds, Is.EqualTo(5f / 6f).Within(0.01f));
        }

        // --- #442: the melee result's 3.6 terms and the strike order ----------------------------------------------

        // #431 Scourge: "for each unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds"
        // - banked on the defender by AssignWoundsStage; the estimate's counted wounds must equal the bank.
        [Test]
        public async Task Scourge_CountedWounds_MatchTheEnginesBank()
        {
            var weapon = Rifle(attacks: 2);
            weapon.AttachRuleDefinition(new ResolvedRule("Scourge", WeaponRules36Definitions.Scourge));
            var attacker = MakeUnit(5, weapon, atX: 0f);
            var defender = MakeUnit(10, null, defense: 4, atX: 10f);
            float distance = Distance(attacker, defender);

            float engineWounds = await EngineVolleyWounds(attacker, defender, weapon, 5);
            float engineCounted = CountedWounds.Peek(defender.GetValue());
            (float wounds, float counted, _) = CombatMath.EstimateVolleyWithCounted(_ctx.RuleEvaluator, attacker,
                defender, weapon, 5, new AttackContext(distance), new List<string>());

            Assert.That(engineCounted, Is.GreaterThan(0f), "scene check: Scourge banked something");
            Assert.That(counted, Is.EqualTo(engineCounted).Within(Math.Max(0.05f, 0.02f * engineCounted)));
            Assert.That(wounds, Is.EqualTo(engineWounds).Within(Math.Max(0.05f, 0.02f * engineWounds)));
        }

        [Test]
        public void EstimateMelee_Margin_CountsScourge_AndWoundsTakenEarlierInTheActivation()
        {
            var scourgeBlade = Blade(attacks: 3);
            scourgeBlade.AttachRuleDefinition(new ResolvedRule("Scourge", WeaponRules36Definitions.Scourge));
            var attacker = MakeUnit(3, scourgeBlade, atX: 0f);
            var defender = MakeUnit(5, Blade(), atX: 1f);

            MeleeEstimate fresh = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);
            Assert.That(fresh.AttackerAttack.ExpectedCountedWounds, Is.GreaterThan(0f));
            Assert.That(fresh.ExpectedWoundsMargin, Is.EqualTo(fresh.AttackerAttack.ExpectedWounds
                + fresh.AttackerAttack.ExpectedCountedWounds - fresh.DefenderReturn.ExpectedWounds).Within(0.0001f));

            // #419: the defender already took 2 wounds this activation (a Breath Attack, say) - they count for the result.
            WoundsTakenThisActivation.Add(defender.GetValue(), 2f);
            MeleeEstimate afterBreath = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);
            Assert.That(afterBreath.ExpectedWoundsMargin, Is.EqualTo(fresh.ExpectedWoundsMargin + 2f).Within(0.0001f));
        }

        // #431: OPR 3.6 prints Unwieldy ("strikes last when charging") on the WEAPON; the strike-order mirror now
        // fans the charger out with its melee weapons, as DetermineStrikeOrderStage does.
        [Test]
        public void EstimateMelee_UnwieldyChargerWeapon_StrikesLast()
        {
            var axe = Blade(attacks: 3);
            axe.AttachRuleDefinition(new ResolvedRule("Unwieldy", WeaponRules36Definitions.Unwieldy));
            var attacker = MakeUnit(3, axe, atX: 0f);
            var defender = MakeUnit(3, Blade(), atX: 1f);
            var unarmed = MakeUnit(3, null, atX: 1f);

            Assert.That(CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender).DefenderStrikesFirst, Is.True);
            Assert.That(CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, unarmed).DefenderStrikesFirst, Is.False,
                "a defender that cannot swing is not put ahead of the charger (the stage's guard)");
        }

        // #435: Great Sergeant's AP(+2) rides the champion's weapon copy like Sergeant's AP(+1), so CombatMath prices
        // it through the batch's own rules - pinned against the stages.
        [Test]
        public async Task GreatSergeant_ChampionCopy_MatchesEngine()
        {
            var copy = Rifle(attacks: 2);
            copy.AttachRuleDefinition(new ResolvedRule("Great Sergeant", AofSweep36Definitions.GreatSergeant()));
            var champion = MakeUnit(1, copy, atX: 0f);
            var defender = MakeUnit(5, null, defense: 3, atX: 10f);

            await AssertVolleyPinned(champion, defender, copy, weaponCount: 1);
            float plain = CombatMath.EstimateVolley(_ctx.RuleEvaluator, champion, defender, Rifle(attacks: 2), 1,
                new AttackContext(Distance(champion, defender)), new List<string>());
            float sergeant = CombatMath.EstimateVolley(_ctx.RuleEvaluator, champion, defender, copy, 1,
                new AttackContext(Distance(champion, defender)), new List<string>());
            Assert.That(sergeant, Is.GreaterThan(plain + 0.01f), "AP(+2) into Defense 3");
        }

        // #434 Hazardous: "on unmodified results of 1 to hit this model takes one wound, or X wounds if this weapon has
        // Blast(X)/Deadly(X)" - each die's wounds on one firing model, the excess lost. The estimate's self-wounds are
        // the engine's 1s (RollToHitStage's carried count) at min(X, a model's wounds) each.
        [TestCase(1, 1f)]
        [TestCase(3, 3f)]
        public async Task Hazardous_SelfWounds_MatchTheEnginesOnes_CappedPerModel(int woundsPerModel, float perDieExpected)
        {
            var cannon = Rifle(attacks: 3);
            cannon.AttachRuleDefinition(new ResolvedRule("Blast", CoreRuleCatalog.Blast,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            cannon.AttachRuleDefinition(new ResolvedRule("Hazardous", HazardousBlast()));
            var crew = MakeUnit(2, cannon, atX: 0f, woundsPerModel: woundsPerModel);
            var defender = MakeUnit(10, null, atX: 10f);

            var metadata = new CombatMetadata(_ctx, crew, defender, cannon, 2);
            metadata.AddResult(new CoverCheckResults(0));
            await RunStage(new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            Assert.That(metadata.QueryForResult(out RollToHitResults hits), Is.True);
            float engineOnes = hits.SelfWounds / hits.SelfWoundsPerDie;

            (_, _, float selfWounds) = CombatMath.EstimateVolleyWithCounted(_ctx.RuleEvaluator, crew, defender, cannon,
                2, new AttackContext(Distance(crew, defender)), new List<string>());

            Assert.That(engineOnes, Is.EqualTo(1f).Within(0.0001f), "scene check: 6 dice, one natural 1 expected");
            Assert.That(selfWounds, Is.EqualTo(engineOnes * perDieExpected).Within(0.0001f));
        }

        // --- #442 (#427 / #434): granted roll modifiers and attacker-bonus markers, read without spending -------

        [Test]
        public async Task GrantedHitBuff_AndGrantedDefenseBuff_MatchEngine()
        {
            var weapon = Rifle(attacks: 2, ap: 1);
            var attacker = MakeUnit(4, weapon, atX: 0f);
            var defender = MakeUnit(6, null, defense: 4, atX: 10f);
            // Duration grants (cleared at round end), so the engine's ConsumeNet leaves them for the estimate too.
            attacker.GetValue().Tokens.AddToken(new Token(TokenType.HitRollModifier, 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(1)));
            defender.GetValue().Tokens.AddToken(new Token(TokenType.SaveRollModifier, 1,
                new TokenClearTrigger.RoundEnd(), Payload: new TokenPayload.StatModifier(1)));

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 4);
        }

        [Test]
        public void OneShotGrant_IsPricedWithoutBeingSpent()
        {
            var weapon = Rifle(attacks: 2);
            var attacker = MakeUnit(4, weapon, atX: 0f);
            var defender = MakeUnit(6, null, atX: 10f);
            float plain = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, weapon, 4,
                new AttackContext(Distance(attacker, defender)), new List<string>());
            attacker.GetValue().Tokens.AddToken(new Token(TokenType.HitRollModifier, 1,
                new TokenClearTrigger.FirstTrigger(), Payload: new TokenPayload.StatModifier(1)));

            float buffed = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender, weapon, 4,
                new AttackContext(Distance(attacker, defender)), new List<string>());

            Assert.That(buffed, Is.GreaterThan(plain + 0.01f), "+1 to hit next time: priced");
            Assert.That(attacker.GetValue().Tokens.GetTokenCount(TokenType.HitRollModifier), Is.EqualTo(1),
                "an estimate must never spend the grant");
        }

        [Test]
        public async Task PersistentTargetMarkers_MatchEngine()
        {
            var weapon = Rifle(attacks: 2);
            var attacker = MakeUnit(4, weapon, atX: 0f);
            var defender = MakeUnit(6, null, defense: 3, atX: 10f);
            defender.GetValue().Tokens.AddToken(new Token(TokenType.PersistentHitBonus, 1,
                new TokenClearTrigger.ManualOnly()));
            defender.GetValue().Tokens.AddToken(new Token(TokenType.PersistentApBonus, 1,
                new TokenClearTrigger.ManualOnly()));

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 4);
        }

        // A 3.6 Tag token is worth one +1 per attack - as much as one persistent marker on a single volley.
        [Test]
        public void TagToken_IsOneMarkerPerAttack()
        {
            var weapon = Rifle(attacks: 2);
            var attacker = MakeUnit(4, weapon, atX: 0f);
            var tagged = MakeUnit(6, null, defense: 3, atX: 10f);
            tagged.GetValue().Tokens.AddToken(new Token(TokenType.SingleSpendApBonus, 2, new TokenClearTrigger.ManualOnly()));
            var targeted = MakeUnit(6, null, defense: 3, atX: 10f);
            targeted.GetValue().Tokens.AddToken(new Token(TokenType.PersistentApBonus, 1, new TokenClearTrigger.ManualOnly()));

            float viaTag = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, tagged, weapon, 4,
                new AttackContext(10f), new List<string>());
            float viaTarget = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, targeted, weapon, 4,
                new AttackContext(10f), new List<string>());

            Assert.That(viaTag, Is.EqualTo(viaTarget).Within(0.0001f));
            Assert.That(tagged.GetValue().Tokens.GetTokenCount(TokenType.SingleSpendApBonus), Is.EqualTo(2), "unspent");
        }

        // #431 Pierce ignores positive Defense modifiers - cover's +1 included, as DetermineSaveRollsNeededStage drops it.
        [Test]
        public async Task PierceWeapon_IgnoresCover_MatchesEngine()
        {
            var weapon = Rifle(attacks: 2, ap: 1);
            weapon.AttachRuleDefinition(new ResolvedRule("Pierce", WeaponRules36Definitions.Pierce));
            var attacker = MakeUnit(4, weapon, atX: 0f);
            var defender = MakeUnit(6, null, defense: 4, atX: 6f);

            await AssertVolleyPinned(attacker, defender, weapon, weaponCount: 4, defenderInCover: true);
        }

        // --- #442 (#434 Ravage / Regenerative Strength): the pre-strike auto-wound pools ------------------------

        // A charger whose only melee output is Ravage: the estimate's attack is the pool, unsaveable, through the
        // defender's wound-side rules - pinned against the REAL ResolveRavageWoundsStage (with and without a 5+ ignore).
        [TestCase(false)]
        [TestCase(true)]
        public async Task Ravage_Pool_MatchesTheRealStage(bool regenerating)
        {
            var charger = MakeUnit(2, null, atX: 0f);
            charger.GetValue().AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            var defender = MakeUnit(10, null, defense: 2, atX: 1f);
            if (regenerating) AttachUnitRule(defender, "Regeneration", CoreRuleCatalog.Regeneration);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, charger, defender);

            var stage = new ResolveRavageWoundsStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnRavageResolved.Bind("done");
            var combat = new CombatActionContext(_ctx, charger, isMelee: true, isCharging: true);
            combat.SetDefender(defender);
            await stage.Enter(combat);
            float engine = _requester.Captured?.TotalWoundsToAssign ?? 0f;

            // 2 models x Ravage(3) = 6 dice at 6+ = 1 wound, no save (Defense 2 would stop most of it).
            Assert.That(estimate.AttackerAttack.ExpectedWounds, Is.EqualTo(engine).Within(0.05f));
            Assert.That(engine, Is.EqualTo(regenerating ? 2f / 3f : 1f).Within(0.01f), "scene check: a 5+ ignore keeps 2/3");
        }

        [Test]
        public void Ravage_OncePerRound_AndNotWhileFatigued()
        {
            var charger = MakeUnit(2, null, atX: 0f);
            charger.GetValue().AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            var defender = MakeUnit(10, null, atX: 1f);
            Assert.That(CombatMath.EstimateMelee(_ctx.RuleEvaluator, charger, defender).AttackerAttack.ExpectedWounds,
                Is.EqualTo(1f).Within(0.0001f));

            FatigueUtilities.ApplyFatigued(charger.GetValue());
            Assert.That(CombatMath.EstimateMelee(_ctx.RuleEvaluator, charger, defender).AttackerAttack.ExpectedWounds,
                Is.EqualTo(0f), "'unless this unit is Fatigued'");

            var rested = MakeUnit(2, null, atX: 0f);
            rested.GetValue().AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(3) }));
            rested.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.StrikeDiceUsed));
            Assert.That(CombatMath.EstimateMelee(_ctx.RuleEvaluator, rested, defender).AttackerAttack.ExpectedWounds,
                Is.EqualTo(0f), "'once per round': already rolled this round");
        }

        // The struck-back defender rolls its own pool (StrikeBackStage's twin), before its swings.
        [Test]
        public void Ravage_DefendersPool_LandsOnTheCharger()
        {
            var charger = MakeUnit(5, Blade(), atX: 0f);
            var defender = MakeUnit(2, Blade(), atX: 1f);
            MeleeEstimate plain = CombatMath.EstimateMelee(_ctx.RuleEvaluator, charger, defender);
            defender.GetValue().AttachRuleDefinition(new ResolvedRule("Ravage", CoreRuleCatalog.Ravage,
                new RuleArgument[] { new RuleArgument.Int(3) }));

            MeleeEstimate ravaging = CombatMath.EstimateMelee(_ctx.RuleEvaluator, charger, defender);

            Assert.That(ravaging.DefenderReturn.ExpectedWounds,
                Is.EqualTo(plain.DefenderReturn.ExpectedWounds + 1f).Within(0.0001f));
        }

        private static SpecialRuleDefinition HazardousBlast() => new("Hazardous",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.UnmodifiedRollEquals(1),
                    new Effect.SelfWoundOnUnmodifiedRoll(OnRollValue: 1, CountFromBlastOrDeadly: true),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(), Scope: ERuleScope.Weapon);

        // --- Harness --------------------------------------------------------------------------------

        /// <summary>
        /// Runs the REAL stage chain and the CombatMath estimate for one volley and asserts the plan's
        /// 6.3 tolerance. Cover is seeded the way CoverCheckStage would produce it (majority cover
        /// gated by the engine's own IgnoresCover query), since the stage itself is geometry-driven.
        /// </summary>
        private async Task AssertVolleyPinned(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            Weapon weapon, int weaponCount, bool isMelee = false, bool isCharging = false,
            bool defenderInCover = false, bool attackerMoved = false)
        {
            float distance = Distance(attacker, defender);
            float engineWounds = await EngineVolleyWounds(attacker, defender, weapon, weaponCount, isMelee,
                isCharging, defenderInCover, attackerMoved);

            // Estimate side - same evaluator, same units, caller-supplied distance/cover. #423: a melee volley prices
            // the defender's Parry pool the way the engine chain (which runs ParryHitsStage) rolls it - null when the
            // defender has no carrier, so every pre-Parry pin is unchanged.
            var notes = new List<string>();
            ParryBudget? parry = isMelee
                ? CombatMath.ParryBudgetFor(_ctx.RuleEvaluator, attacker.GetValue(), defender.GetValue())
                : null;
            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender,
                weapon, weaponCount,
                new AttackContext(distance, attackerMoved, defenderInCover, isMelee, isCharging), notes,
                parryBudget: parry);
            float estimateCapped = Math.Min(estimate, defender.GetValue().RemainingWounds);

            float tolerance = Math.Max(0.05f, 0.02f * engineWounds);
            Assert.That(estimateCapped, Is.EqualTo(engineWounds).Within(tolerance),
                $"CombatMath diverged from the engine (notes: {string.Join("; ", notes)})");
        }

        // The REAL stage chain for one volley; returns the wounds that landed (#401). Cover is seeded the way
        // CoverCheckStage would produce it (majority cover gated by the engine's own IgnoresCover query).
        private async Task<float> EngineVolleyWounds(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            Weapon weapon, int weaponCount, bool isMelee = false, bool isCharging = false,
            bool defenderInCover = false, bool attackerMoved = false, float chargeOriginInches = 0f)
        {
            int coverBonus = defenderInCover && !isMelee
                && !SightRuleQueries.IgnoresCover(attacker.GetValue(), weapon, _ctx.RuleEvaluator, attackerMoved) ? 1 : 0;

            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount,
                attackerMoved, isMelee, isCharging, chargeOriginDistanceInches: chargeOriginInches);
            metadata.AddResult(new CoverCheckResults(coverBonus));
            await RunStage(new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            // #423: the melee chain's Parry step (a no-op for shooting and for a defender without the rule).
            await RunStage(new ParryHitsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToSaveStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults engineResults), Is.True,
                "the engine chain must produce an AssignWoundsResults");
            return engineResults.TotalAssignedWounds;
        }

        private static async Task RunStage<TResult, TSelf>(
            CombatStage<TResult, TSelf, ICombatMetadata> stage, CombatMetadata metadata)
            where TSelf : CombatStage<TResult, TSelf, ICombatMetadata>
        {
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);
        }

        private float Distance(DataBinding<UnitData> attacker, DataBinding<UnitData> defender) =>
            UnitCompareUtilities.MinDistanceBetweenUnits(attacker.GetValue(), defender.GetValue(),
                out _, out _, includeVertical: true);

        private static Weapon Rifle(int attacks = 1, int ap = 0, float range = 24f) =>
            new Weapon("Rifle", rangeInches: range, attacks: attacks, armorPenetration: ap);

        private static Weapon Blade(int attacks = 2, int ap = 0) =>
            new Weapon("Blade", rangeInches: 0f, attacks: attacks, armorPenetration: ap);

        // #423: the AoF Hand Weapon shape - a melee weapon carrying Parry(X).
        private static Weapon HandWeapon(int parry)
        {
            var weapon = new Weapon("Hand Weapon", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Parry", CoreRuleCatalog.Parry,
                new RuleArgument[] { new RuleArgument.Int(parry) }));
            return weapon;
        }

        private static void AttachUnitRule(DataBinding<UnitData> unit, string name,
            SpecialRuleDefinition definition) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, definition));

        /// <summary>
        /// A unit at x = <paramref name="atX"/>: every model carries <paramref name="weapon"/> (when
        /// given) so weapon batching and batch-owner semantics are real, and models get
        /// <paramref name="woundsPerModel"/> max wounds (Tough's load-time effect).
        /// </summary>
        private DataBinding<UnitData> MakeUnit(int modelCount, Weapon? weapon,
            int quality = 4, int defense = 4, float atX = 20f, int woundsPerModel = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: weapon == null ? new List<Weapon>() : new List<Weapon> { weapon },
                    initialPosition: new Position(atX, i * 1.2f),
                    gameDataStore: _store);
                if (woundsPerModel > 1)
                    model.SetMaxWounds(woundsPerModel);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: quality, defense: defense,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
