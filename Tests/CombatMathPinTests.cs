using FDG.Ai.Tactician;
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

        [Test]
        public async Task Crack_PerHitAp_MatchesEngine()
        {
            var weapon = Rifle();
            weapon.AttachRuleDefinition(new ResolvedRule("Crack", CoreRuleCatalog.Crack));
            var attacker = MakeUnit(3, weapon);
            var defender = MakeUnit(5, null);

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

            // Impact(4): 4 dice at 2+ -> 4 * 5/6 hits; D4 save at AP0 -> half fail.
            // Swings: 2 blades x 2 attacks at Q4 -> 2 hits -> 1 wound. Total 2.667 + margin for exactness.
            float impactWounds = 4f * (5f / 6f) * 0.5f;
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
                new RuleArgument[] { new RuleArgument.Int(4) }));
            var spear = new Weapon("Spear", rangeInches: 0f, attacks: 0, armorPenetration: 0);
            spear.AttachRuleDefinition(new ResolvedRule("Brace", CoreRuleCatalog.Brace,
                new RuleArgument[] { new RuleArgument.Int(1) }));
            var defender = MakeUnit(3, spear, atX: 1f);

            MeleeEstimate estimate = CombatMath.EstimateMelee(_ctx.RuleEvaluator, attacker, defender);

            // Impact(4) - Brace(1) x 3 spearmen = 1 die at 2+, half saved; swings 2 x 2 at Q4, half saved.
            float impactWounds = 1f * (5f / 6f) * 0.5f;
            float swingWounds = 2f * 2f * 0.5f * 0.5f;
            Assert.That(estimate.AttackerAttack.ExpectedWounds,
                Is.EqualTo(impactWounds + swingWounds).Within(0.001f));
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
            int coverBonus = defenderInCover && !isMelee
                && !SightRuleQueries.IgnoresCover(attacker.GetValue(), weapon, _ctx.RuleEvaluator, attackerMoved) ? 1 : 0;

            // Engine side.
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount,
                attackerMoved, isMelee, isCharging);
            metadata.AddResult(new CoverCheckResults(coverBonus));
            await RunStage(new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new RollToSaveStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);
            await RunStage(new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>()), metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults engineResults), Is.True,
                "the engine chain must produce an AssignWoundsResults");
            float engineWounds = engineResults.TotalAssignedWounds; // what landed (#401)

            // Estimate side - same evaluator, same units, caller-supplied distance/cover.
            var notes = new List<string>();
            float estimate = CombatMath.EstimateVolley(_ctx.RuleEvaluator, attacker, defender,
                weapon, weaponCount,
                new AttackContext(distance, attackerMoved, defenderInCover, isMelee, isCharging), notes);
            float estimateCapped = Math.Min(estimate, defender.GetValue().RemainingWounds);

            float tolerance = Math.Max(0.05f, 0.02f * engineWounds);
            Assert.That(estimateCapped, Is.EqualTo(engineWounds).Within(tolerance),
                $"CombatMath diverged from the engine (notes: {string.Join("; ", notes)})");
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
