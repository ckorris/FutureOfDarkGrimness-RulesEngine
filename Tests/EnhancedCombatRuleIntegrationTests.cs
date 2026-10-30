using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #433 - the OPR 3.6 "Enhanced X" family through the REAL stages: the keep-best folds (Enhanced Guardian's AP(-2),
    // Enhanced Plaguebound's 5-6 wound ignore, Enhanced Steadfast's 3+ recovery, Enhanced Mischievous' 5-6 re-roll,
    // Enhanced Harassing's 6" post-combat move), the activation move (Enhanced Wolfborn's 2D3"). For each shape: base +
    // Enhanced applies exactly the Enhanced value once (the no-stack guard), and Enhanced without its base is inert.
    [TestFixture]
    public class EnhancedCombatRuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new ProbabilisticDiceRoller());
        }

        // --- the base rules the deltas sit on (the supplement's shapes) ----------------------------------------------

        private static class Bases
        {
            public static SpecialRuleDefinition Guardian => GdfSweep36Definitions.Guardian();

            /// <summary>Plaguebound / Self-Repair / Protected / Cursed Undead: "ignore wounds on 6+" on most models.</summary>
            public static SpecialRuleDefinition Plaguebound => new("Plaguebound",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.MostModelsHaveThisRule(),
                        new Effect.IgnoreWoundOnRoll(6), ELifetime.ThisAttack, ERuleSeat.Subject),
                },
                System.Array.Empty<ActivatedAbility>());

            /// <summary>Steadfast / Vale Oath: "On a 4+, it stops being Shaken."</summary>
            public static SpecialRuleDefinition Steadfast => new("Steadfast",
                new[]
                {
                    new HookEntry(EHookID.Round_OnRoundStart,
                        new Condition.And(new Condition.TokenPresent(TokenType.Shaken), new Condition.MostModelsHaveThisRule()),
                        new Effect.ClearTokenOnRoll(TokenType.Shaken, 4), ELifetime.ThisRound),
                },
                System.Array.Empty<ActivatedAbility>());

            /// <summary>Mischievous / Scrapper / Vicious / Bestial: a weapon rule, "re-roll unmodified Defense 6s".</summary>
            public static SpecialRuleDefinition Mischievous => new("Mischievous",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.Always(),
                        new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue()), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon);

            /// <summary>Targeting Visor / Precision Marksman / Buccaneer: +1 to hit when shooting over 9".</summary>
            public static SpecialRuleDefinition TargetingVisor => new("Targeting Visor",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.And(new Condition.Not(new Condition.IsMelee()), new Condition.DistanceGreaterThan(9f)),
                        new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>());

            /// <summary>Bloodborn / Clan Warrior / Primal / Predator / Royal Warrior: one extra hit per natural 6 (#434 D7).</summary>
            public static SpecialRuleDefinition Bloodborn => new("Bloodborn",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.UnmodifiedRollEquals(6),
                        new Effect.AddExtraHit(6, 1), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>());

            /// <summary>Wolfborn / Nimble / Rapid Blink / Wave-Step: the D3" activation move (#434 D8).</summary>
            public static SpecialRuleDefinition Wolfborn => new("Wolfborn",
                new[]
                {
                    new HookEntry(EHookID.Activation_OnActivationStart, new Condition.MostModelsHaveThisRule(),
                        new Effect.RepositionAtActivation(new DiceExpression.D3(), 0, Move: true), ELifetime.ThisActivation),
                },
                System.Array.Empty<ActivatedAbility>());
        }

        // --- B2: Enhanced Guardian (and Fortified / Ossified / Warden): AP(-2) ----------------------------------------

        [TestCase(1, 4)]
        [TestCase(2, 4)]
        [TestCase(3, 5)]
        public async Task EnhancedGuardian_WithGuardian_CountsHitsAsApMinusTwo_FlooredAtZero(int ap, int expectedSave)
        {
            DataBinding<UnitData> defender = MakeUnit("Custodians", 10f, 1, Bases.Guardian, Enhanced36Definitions.ApMinusTwo());

            Assert.That(await SaveNeeded(new Weapon("Gun", 24f, 1, ap), defender, 10f), Is.EqualTo(expectedSave),
                $"AP({ap}) less 2, never less 3 (the delta once) and never below AP(0)");
        }

        [Test]
        public async Task EnhancedGuardian_WithoutGuardian_IsInert()
        {
            DataBinding<UnitData> defender = MakeUnit("Pretenders", 10f, 1, Enhanced36Definitions.ApMinusTwo());

            Assert.That(await SaveNeeded(new Weapon("Gun", 24f, 1, 1), defender, 10f), Is.EqualTo(5), "AP(1) lands");
        }

        // --- B2: Enhanced Plaguebound (and Self-Repair / Protected / Cursed Undead): ignore on 5-6 -------------------

        [Test]
        public async Task EnhancedPlaguebound_WithPlaguebound_IgnoresWoundsOnFiveAndSix()
        {
            DataBinding<UnitData> defender = MakeUnit("Daemons", 10f, 1, Bases.Plaguebound, Enhanced36Definitions.IgnoreFivePlus());

            Assert.That(await WoundsFromOneFailedSave(defender), Is.EqualTo(4f / 6f).Within(0.0001f),
                "one roll per wound at the best threshold (5+), not a 6+ roll and then a 5+ roll");
        }

        [Test]
        public async Task EnhancedPlaguebound_WithoutPlaguebound_IsInert()
        {
            DataBinding<UnitData> defender = MakeUnit("Pretenders", 10f, 1, Enhanced36Definitions.IgnoreFivePlus());

            Assert.That(await WoundsFromOneFailedSave(defender), Is.EqualTo(1f).Within(0.0001f));
        }

        // --- B2: Enhanced Steadfast (and Vale Oath): recover on 3+ -------------------------------------------------------

        [Test]
        public void EnhancedSteadfast_WithSteadfast_IsOneRecoveryRoll_AtThreePlus()
        {
            var harness = new TestRuleHarness();
            harness.Register(Bases.Steadfast);
            harness.Register(Enhanced36Definitions.RecoverThreePlus());
            IUnit unit = harness.BuildUnit("P1", 3, "Steadfast", "Enhanced Steadfast");
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            var sink = new TokenClearRollSink();
            sink.ApplyFrom(harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit)));

            Assert.That(sink.Entries, Is.EqualTo(new[] { (TokenType.Shaken, 3) }),
                "the 4+ and the 3+ fold to ONE roll at 3+ - never a second chance");
        }

        [Test]
        public async Task EnhancedSteadfast_TheFoldedRoll_ClearsOnAThree()
        {
            var harness = new TestRuleHarness(new FixedDiceRoller(3));
            harness.Register(Bases.Steadfast);
            harness.Register(Enhanced36Definitions.RecoverThreePlus());
            IUnit unit = harness.BuildUnit("P1", 3, "Steadfast", "Enhanced Steadfast");
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            await TokenClearRolls.ResolveAsync(unit, harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit)),
                new GameOperationServices(harness.GameContext));

            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False, "a 3 recovers an Enhanced Steadfast unit");
        }

        [Test]
        public void EnhancedSteadfast_WithoutSteadfast_RollsNothing()
        {
            var harness = new TestRuleHarness();
            harness.Register(Enhanced36Definitions.RecoverThreePlus());
            IUnit unit = harness.BuildUnit("P1", 3, "Enhanced Steadfast");
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            Assert.That(harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit)), Is.Empty);
        }

        // --- B2: Enhanced Mischievous (and Scrapper / Vicious / Bestial): re-roll 5-6 ---------------------------------

        [Test]
        public void EnhancedMischievous_WithMischievousOnTheWeapon_RerollsSavesOfFiveAndSix()
        {
            var harness = new TestRuleHarness();
            IUnit goblins = harness.BuildUnit("P1", 3);
            IUnit enemy = harness.BuildUnit("P2", 1);
            var blunderbuss = new Weapon("Blunderbuss", 12f, 1, 0);
            harness.AttachWeaponRule(blunderbuss, Bases.Mischievous);
            ((ModelData)goblins.Models[0]).Weapons.Add(blunderbuss);
            harness.AttachRule(goblins, Enhanced36Definitions.RerollFivePlus());

            var sink = new RerollSink();
            sink.ApplyFrom(harness.EvaluateAll(new SaveRollCompleteContext(goblins, enemy, TestDice.Faces(5, 6)),
                RuleParticipant.Actor(goblins, blunderbuss), RuleParticipant.Subject(enemy)));

            Assert.That(sink.RerollSavesAtOrAbove, Is.EqualTo(5), "the widest band wins: 5-6, one re-roll pass");
        }

        [Test]
        public void EnhancedMischievous_WithoutMischievous_IsInert()
        {
            var harness = new TestRuleHarness();
            IUnit goblins = harness.BuildUnit("P1", 3);
            IUnit enemy = harness.BuildUnit("P2", 1);
            harness.AttachRule(goblins, Enhanced36Definitions.RerollFivePlus());

            var sink = new RerollSink();
            sink.ApplyFrom(harness.EvaluateAll(new SaveRollCompleteContext(goblins, enemy, TestDice.Faces(5, 6)),
                RuleParticipant.Actor(goblins, new Weapon("Club", 0f, 1, 0)), RuleParticipant.Subject(enemy)));

            Assert.That(sink.RerollSavesAtOrAbove, Is.Null);
        }

        // --- B2: Enhanced Harassing (and Guerrilla / Hit & Run): 6" instead of 3" --------------------------------------

        [Test]
        public async Task EnhancedHarassing_WithHarassing_OffersOneSixInchMove()
        {
            var requester = new CapturingMoveRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = InArmy(MakeUnit("Raiders", 0f, 3, CoreRuleCatalog.Harassing,
                Enhanced36Definitions.PostCombatSix()));
            IUnit u = unit.GetValue();

            await PostCombatMoveGate.OfferIfAvailable(ctx, u, ctx.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(u), RuleParticipant.Actor(u)));

            Assert.That(requester.Asked, Is.EqualTo(1), "the 3\" and the 6\" coalesce into ONE move");
            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(6f).Within(0.001f), "at the largest budget");
        }

        [Test]
        public async Task EnhancedHarassing_WithoutHarassing_OffersNothing()
        {
            var requester = new CapturingMoveRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> unit = InArmy(MakeUnit("Pretenders", 0f, 3, Enhanced36Definitions.PostCombatSix()));
            IUnit u = unit.GetValue();

            await PostCombatMoveGate.OfferIfAvailable(ctx, u, ctx.RuleEvaluator.EvaluateAll(
                new PostMeleeActionContext(u), RuleParticipant.Actor(u)));

            Assert.That(requester.Asked, Is.EqualTo(0));
        }

        // --- B3: Enhanced Wolfborn (and Nimble / Rapid Blink / Wave-Step): 2D3" ---------------------------------------

        [Test]
        public async Task EnhancedWolfborn_WithWolfborn_OffersOneMove_OfTwoD3()
        {
            DataBinding<UnitData> unit = MakeUnit("Hunters", 10f, 2, Bases.Wolfborn, Enhanced36Definitions.TwoD3());
            var requester = new CapturingMoveRequester();

            await RunActivationStart(unit, requester, new FixedDiceRoller(2));

            Assert.That(requester.Asked, Is.EqualTo(1), "the two D3 entries sum into ONE move");
            Assert.That(requester.Captured!.MaxDistanceInches, Is.EqualTo(4f).Within(0.001f), "D3 + D3, both fixed to 2");
        }

        [Test]
        public async Task EnhancedWolfborn_WithoutWolfborn_OffersNothing()
        {
            DataBinding<UnitData> unit = MakeUnit("Pretenders", 10f, 2, Enhanced36Definitions.TwoD3());
            var requester = new CapturingMoveRequester();

            await RunActivationStart(unit, requester, new FixedDiceRoller(2));

            Assert.That(requester.Asked, Is.EqualTo(0));
        }

        // --- C: the "always ... (instead of only when over 9"/6")" complements -------------------------------------------
        // Base + Enhanced: the effect at EVERY distance, once - the two arms are disjoint and can never both fire.

        [TestCase(10f, false, 0f)]
        [TestCase(8f, false, 0f)]
        [TestCase(0.5f, true, 7f)]
        [TestCase(0.5f, true, 5f)]
        [TestCase(0.5f, true, 0f)]
        public async Task EnhancedChangebound_WithChangebound_EnemiesGetMinusOneToHit_Always(float distance, bool melee,
            float origin)
        {
            DataBinding<UnitData> defender = MakeUnit("Daemons", 10f, 3, RangeConditional36Definitions.MinusHit(),
                Enhanced36Definitions.MinusHit());

            Assert.That(await HitNeeded(defender, distance, melee, charging: origin > 0f, origin), Is.EqualTo(5),
                "-1 once: over range from the base arm, inside it (and on a strike-back) from the Enhanced arm");
        }

        [Test]
        public async Task EnhancedChangebound_WithoutChangebound_IsInert()
        {
            DataBinding<UnitData> defender = MakeUnit("Pretenders", 10f, 3, Enhanced36Definitions.MinusHit());

            Assert.That(await HitNeeded(defender, 8f, melee: false, charging: false, origin: 0f), Is.EqualTo(4));
        }

        [TestCase(10f)]
        [TestCase(8f)]
        public async Task EnhancedGuarded_WithGuarded_PlusOneDefense_Always(float distance)
        {
            DataBinding<UnitData> defender = MakeUnit("Guards", 10f, 1, RangeConditional36Definitions.PlusDefense(),
                Enhanced36Definitions.PlusDefense());

            Assert.That(await SaveNeeded(new Weapon("Gun", 24f, 1, 0), defender, distance), Is.EqualTo(3), "+1 once");
        }

        [Test]
        public async Task EnhancedGuarded_WithoutGuarded_IsInert()
        {
            DataBinding<UnitData> defender = MakeUnit("Pretenders", 10f, 1, Enhanced36Definitions.PlusDefense());

            Assert.That(await SaveNeeded(new Weapon("Gun", 24f, 1, 0), defender, 8f), Is.EqualTo(4));
        }

        [TestCase(10f, false, 0f, 5f / 6f)]
        [TestCase(8f, false, 0f, 5f / 6f)]
        [TestCase(0.5f, true, 7f, 5f / 6f)]
        [TestCase(0.5f, true, 5f, 5f / 6f)]
        public async Task EnhancedKnightborn_WithKnightborn_IgnoresShootingAndChargingWounds_OnSixPlus_AtAnyDistance(
            float distance, bool melee, float origin, float expected)
        {
            DataBinding<UnitData> knights = MakeUnit("Knights", 10f, 1, RangeConditional36Definitions.Ward(),
                Enhanced36Definitions.Ward());

            Assert.That(await WoundsFromOneFailedSave(knights, distance, melee, origin), Is.EqualTo(expected).Within(0.0001f),
                "one 6+ roll per wound - never a 6+ and then another 6+");
        }

        [Test]
        public async Task EnhancedKnightborn_LeavesStrikeBackWounds_AndKeepsTheSpellArm()
        {
            DataBinding<UnitData> knights = MakeUnit("Knights", 10f, 1, RangeConditional36Definitions.Ward(),
                Enhanced36Definitions.Ward());

            Assert.That(await WoundsFromOneFailedSave(knights, 0.5f, melee: true, origin: 0f), Is.EqualTo(1f).Within(0.0001f),
                "a strike-back's wound is neither a shooting nor a charging wound");
            Assert.That(await WoundsFromOneFailedSave(knights, 4f, isSpell: true), Is.EqualTo(1f / 6f).Within(0.0001f),
                "spell wounds: the base's 2+ arm alone");
        }

        [Test]
        public async Task EnhancedKnightborn_WithoutKnightborn_IsInert()
        {
            DataBinding<UnitData> pretenders = MakeUnit("Pretenders", 10f, 1, Enhanced36Definitions.Ward());

            Assert.That(await WoundsFromOneFailedSave(pretenders, 8f), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void EnhancedHiveBond_WithHiveBond_PlusOneToEveryMoraleTest_Once()
        {
            var harness = new TestRuleHarness();
            IUnit swarm = harness.BuildUnit("P1", 4);
            harness.AttachRule(swarm, RangeConditional36Definitions.Morale());
            harness.AttachRule(swarm, Enhanced36Definitions.Morale());

            Assert.That(MoraleNet(harness, swarm, new PreMoraleTestContext(swarm, ShotFromInches: 10f)), Is.EqualTo(1),
                "over range: the base arm alone");
            Assert.That(MoraleNet(harness, swarm, new PreMoraleTestContext(swarm, ShotFromInches: 8f)), Is.EqualTo(1),
                "inside range: the Enhanced arm alone");
            Assert.That(MoraleNet(harness, swarm, new PreMoraleTestContext(swarm, ChargedFromInches: 5f)), Is.EqualTo(1));
            Assert.That(MoraleNet(harness, swarm, new PreMoraleTestContext(swarm)), Is.EqualTo(1),
                "always: a test no attack triggered too");
        }

        [Test]
        public void EnhancedHiveBond_WithoutHiveBond_IsInert()
        {
            var harness = new TestRuleHarness();
            IUnit swarm = harness.BuildUnit("P1", 4);
            harness.AttachRule(swarm, Enhanced36Definitions.Morale());

            Assert.That(MoraleNet(harness, swarm, new PreMoraleTestContext(swarm, ShotFromInches: 8f)), Is.EqualTo(0));
        }

        [TestCase(10f, false, 0f)]
        [TestCase(8f, false, 0f)]
        [TestCase(0.5f, true, 7f)]
        [TestCase(0.5f, true, 0f)]
        public async Task EnhancedDevout_WithDevout_ANaturalSixScoresOneExtraHit_Always(float distance, bool melee, float origin)
        {
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(6));
            DataBinding<UnitData> shooter = MakeUnit("Sisters", 0f, 1, RangeConditional36Definitions.ExtraHit(),
                Enhanced36Definitions.ExtraHit());

            Assert.That(TotalHits(await Hits(new Weapon("Gun", melee ? 0f : 24f, 1, 0), shooter, MakeUnit("Target", 10f, 1),
                distance, melee, charging: origin > 0f, origin)), Is.EqualTo(2f), "the hit plus ONE extra");
        }

        [Test]
        public async Task EnhancedDevout_WithoutDevout_IsInert_AndASpellGetsNothing()
        {
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(6));
            DataBinding<UnitData> pretenders = MakeUnit("Pretenders", 0f, 1, Enhanced36Definitions.ExtraHit());
            DataBinding<UnitData> sisters = MakeUnit("Sisters", 0f, 1, RangeConditional36Definitions.ExtraHit(),
                Enhanced36Definitions.ExtraHit());
            DataBinding<UnitData> target = MakeUnit("Target", 10f, 1);

            Assert.That(TotalHits(await Hits(new Weapon("Gun", 24f, 1, 0), pretenders, target, 8f)), Is.EqualTo(1f));

            // The spell pipeline (SyntheticHitResolution, IsSpell) rolls hits that all show 6 here: inside 9", a spell is
            // neither the base's shot nor one of the Enhanced arm's weapon attacks.
            Place(target, 8f);
            SyntheticHitResolution.Result spell = SyntheticHitResolution.Resolve(_ctx, sisters.GetValue(), target.GetValue(),
                baseHits: 1, new Weapon("Spell", 0f, 1, 0), isSpell: true);
            Assert.That(spell.HitGroups.Sum(g => g.HitCount), Is.EqualTo(1f),
                "a caster's spell hits are not 'unmodified results of 6 to hit'");
            SyntheticHitResolution.Result ability = SyntheticHitResolution.Resolve(_ctx, sisters.GetValue(), target.GetValue(),
                baseHits: 1, new Weapon("Flamer", 0f, 1, 0), isSpell: false);
            Assert.That(ability.HitGroups.Sum(g => g.HitCount), Is.EqualTo(2f), "control: the same dice, not a spell");
        }

        [TestCase(10f, false, 0f)]
        [TestCase(8f, false, 0f)]
        [TestCase(0.5f, true, 7f)]
        [TestCase(0.5f, true, 0f)]
        public async Task EnhancedHavocbound_WithHavocbound_ApPlusOne_Always(float distance, bool melee, float origin)
        {
            DataBinding<UnitData> raider = MakeUnit("Raider", 0f, 1, RangeConditional36Definitions.ApPlusOne(),
                Enhanced36Definitions.ApPlusOne());

            Assert.That(await SaveNeeded(new Weapon("Gun", melee ? 0f : 24f, 1, 0), MakeUnit("Target", 10f, 1), distance,
                raider, melee, charging: origin > 0f, origin), Is.EqualTo(5), "AP(+1) once");
        }

        [Test]
        public async Task EnhancedHavocbound_WithoutHavocbound_IsInert_AndASpellGetsNothing()
        {
            DataBinding<UnitData> pretender = MakeUnit("Pretender", 0f, 1, Enhanced36Definitions.ApPlusOne());
            DataBinding<UnitData> raider = MakeUnit("Raider", 0f, 1, RangeConditional36Definitions.ApPlusOne(),
                Enhanced36Definitions.ApPlusOne());
            DataBinding<UnitData> target = MakeUnit("Target", 10f, 1);

            Assert.That(await SaveNeeded(new Weapon("Gun", 24f, 1, 0), target, 8f, pretender), Is.EqualTo(4));

            Place(target, 8f);
            SyntheticHitResolution.Result spell = SyntheticHitResolution.Resolve(_ctx, raider.GetValue(), target.GetValue(),
                baseHits: 1, new Weapon("Spell", 0f, 1, 0), isSpell: true);
            Assert.That(spell.SaveModifier, Is.EqualTo(0), "'its weapons get AP(+1)': a spell is not one");
            SyntheticHitResolution.Result ability = SyntheticHitResolution.Resolve(_ctx, raider.GetValue(), target.GetValue(),
                baseHits: 1, new Weapon("Flamer", 0f, 1, 0), isSpell: false);
            Assert.That(ability.SaveModifier, Is.EqualTo(-1), "control: the same synthetic hits, not a spell");
        }

        [TestCase(10f, false, 0f)]
        [TestCase(8f, false, 0f)]
        [TestCase(0.5f, true, 7f)]
        [TestCase(0.5f, true, 0f)]
        public async Task EnhancedInfected_WithInfectedOnTheWeapon_OnesToBlockDealOneExtraWound_Always(float distance,
            bool melee, float origin)
        {
            var claws = new Weapon("Claws", melee ? 0f : 24f, 1, 0);
            claws.AttachRuleDefinition(new ResolvedRule("Infected", RangeConditional36Definitions.ExtraWound()));
            DataBinding<UnitData> carrier = MakeUnit("Carrier", 0f, 1, Enhanced36Definitions.ExtraWound());
            ((ModelData)carrier.GetValue().Models[0]).Weapons.Add(claws);   // "on its weapons": the gate reads them there

            Assert.That(await WoundsFromOneFailedSave(MakeUnit("Target", 10f, 1), distance, melee, origin, weapon: claws,
                attacker: carrier, failedFace: 1), Is.EqualTo(2f), "one natural 1 to block: one wound plus ONE extra");
        }

        [Test]
        public async Task EnhancedInfected_WithoutInfected_IsInert_AndASpellGetsNothing()
        {
            var plain = new Weapon("Claws", 24f, 1, 0);
            var claws = new Weapon("Claws", 24f, 1, 0);
            claws.AttachRuleDefinition(new ResolvedRule("Infected", RangeConditional36Definitions.ExtraWound()));
            DataBinding<UnitData> pretender = MakeUnit("Pretender", 0f, 1, Enhanced36Definitions.ExtraWound());
            ((ModelData)pretender.GetValue().Models[0]).Weapons.Add(plain);
            DataBinding<UnitData> carrier = MakeUnit("Carrier", 0f, 1, Enhanced36Definitions.ExtraWound());
            ((ModelData)carrier.GetValue().Models[0]).Weapons.Add(claws);

            Assert.That(await WoundsFromOneFailedSave(MakeUnit("Target", 10f, 1), 8f, weapon: plain, attacker: pretender,
                failedFace: 1), Is.EqualTo(1f), "no Infected anywhere on the unit");
            Assert.That(await WoundsFromOneFailedSave(MakeUnit("Target", 10f, 1), 8f, weapon: claws, attacker: carrier,
                failedFace: 1, isSpell: true), Is.EqualTo(1f), "'hits from weapons': a spell's are not");
        }

        [TestCase("AP(+1)", 10f, 0, -1)]
        [TestCase("AP(+1)", 8f, 0, -1)]
        [TestCase("+1 to hit", 10f, 1, 0)]
        [TestCase("+1 to hit", 8f, 1, 0)]
        public void EnhancedVersatileWarrior_WithThePick_AppliesIt_Always(string label, float distance, int expectedHit,
            int expectedSave)
        {
            var harness = new TestRuleHarness();
            harness.Register(RangeConditional36Definitions.Pick());
            harness.Register(RangeConditional36Definitions.PickPiercing());
            harness.Register(RangeConditional36Definitions.PickPrecision());
            harness.Register(Enhanced36Definitions.Pick());
            IUnit warriors = harness.BuildUnit("P1", 3, "Versatile Warrior", "Enhanced Versatile Warrior");
            IUnit enemy = harness.BuildUnit("P2", 1);

            AbilityOffer offer = harness.OfferAbilities(new ActivationStartContext(warriors))
                .Single(o => o.Ability.Label == label);
            OperationApplier.ApplyTokenOperations(harness.Accept(offer, warriors));

            var hit = new RollModifierSink();
            hit.ApplyFrom(harness.Evaluate(warriors, ERuleSeat.Actor, new HitRollModifierContext(warriors, enemy, distance)));
            var save = new RollModifierSink();
            save.ApplyFrom(harness.Evaluate(warriors, ERuleSeat.Actor,
                new HitRollCompleteContext(warriors, enemy, TestDice.Faces(4), distance)));

            Assert.That(hit.Net(ERollKind.Hit), Is.EqualTo(expectedHit), $"{label} at {distance}\" - once");
            Assert.That(save.Net(ERollKind.Save), Is.EqualTo(expectedSave), $"{label} at {distance}\" - once");
        }

        [Test]
        public void EnhancedVersatileWarrior_WithoutAPick_GrantsNothing()
        {
            var harness = new TestRuleHarness();
            harness.Register(RangeConditional36Definitions.Pick());
            harness.Register(RangeConditional36Definitions.PickPiercing());
            harness.Register(RangeConditional36Definitions.PickPrecision());
            harness.Register(Enhanced36Definitions.Pick());
            IUnit warriors = harness.BuildUnit("P1", 3, "Versatile Warrior", "Enhanced Versatile Warrior");
            IUnit enemy = harness.BuildUnit("P2", 1);

            var hit = new RollModifierSink();
            hit.ApplyFrom(harness.Evaluate(warriors, ERuleSeat.Actor, new HitRollModifierContext(warriors, enemy, 8f)));
            var save = new RollModifierSink();
            save.ApplyFrom(harness.Evaluate(warriors, ERuleSeat.Actor,
                new HitRollCompleteContext(warriors, enemy, TestDice.Faces(4), 8f)));

            Assert.That(hit.Net(ERollKind.Hit), Is.EqualTo(0));
            Assert.That(save.Net(ERollKind.Save), Is.EqualTo(0));
        }

        [TestCase(10f, false)]
        [TestCase(8f, false)]
        [TestCase(0.5f, true)]
        public async Task EnhancedTargetingVisor_WithTargetingVisor_PlusOneToHit_Always(float distance, bool melee)
        {
            DataBinding<UnitData> shooter = MakeUnit("Shooter", 0f, 1, Bases.TargetingVisor, Enhanced36Definitions.PlusHit());

            Assert.That(await HitNeeded(MakeUnit("Target", 10f, 1), distance, melee, charging: false, origin: 0f, shooter),
                Is.EqualTo(3), "+1 once - over 9\" from the base, otherwise (melee included, D5) from the Enhanced arm");
        }

        [Test]
        public async Task EnhancedTargetingVisor_WithoutTargetingVisor_IsInert()
        {
            DataBinding<UnitData> shooter = MakeUnit("Pretender", 0f, 1, Enhanced36Definitions.PlusHit());

            Assert.That(await HitNeeded(MakeUnit("Target", 10f, 1), 8f, melee: false, charging: false, origin: 0f, shooter),
                Is.EqualTo(4));
        }

        // --- D: Enhanced Bloodborn (and Clan Warrior / Primal / Predator / Royal Warrior): extra hits on successful 5-6 ----
        // The 5 face is the delta, gated on the 5 having HIT (Condition.UnmodifiedRollHits reads the hit threshold the
        // stage split the successes with); the base's one-extra-hit approximation of "+1 attack" (#434 D7) is inherited.
        // FixedFaceDiceRoller, not FixedDiceRoller: the latter's TotalRolls is always 1, so it cannot show a miss.

        [Test]
        public async Task EnhancedBloodborn_WithBloodborn_ASuccessfulFive_ScoresOneExtraHit()
        {
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedFaceDiceRoller(5));
            DataBinding<UnitData> berserkers = MakeUnit("Berserkers", 0f, 1, Bases.Bloodborn, Enhanced36Definitions.HitsOnFive());

            RollToHitResults hits = await Hits(new Weapon("Gun", 24f, 1, 0), berserkers, MakeUnit("Target", 10f, 1), 10f);

            Assert.That(TotalHits(hits), Is.EqualTo(2f), "a 5 against 4+: the hit plus the Enhanced extra");
            Assert.That(HitsOfKind(hits, EHitSourceKind.ExtraHits), Is.EqualTo(1f));
        }

        [Test]
        public async Task EnhancedBloodborn_WithBloodborn_ASix_ScoresOneExtraHit_NotTwo()
        {
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedFaceDiceRoller(6));
            DataBinding<UnitData> berserkers = MakeUnit("Berserkers", 0f, 1, Bases.Bloodborn, Enhanced36Definitions.HitsOnFive());

            Assert.That(TotalHits(await Hits(new Weapon("Gun", 24f, 1, 0), berserkers, MakeUnit("Target", 10f, 1), 10f)),
                Is.EqualTo(2f), "the 6 is the base's face alone - the delta is only the 5");
        }

        [Test]
        public async Task EnhancedBloodborn_AFiveThatMissed_ScoresNothing()
        {
            // A unit needing 6s (a -2 to hit): the 5 is "unmodified 5" but not a "successful hit result".
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedFaceDiceRoller(5));
            DataBinding<UnitData> berserkers = MakeUnit("Berserkers", 0f, 1, Bases.Bloodborn, Enhanced36Definitions.HitsOnFive());

            Assert.That(TotalHits(await Hits(new Weapon("Gun", 24f, 1, 0), berserkers, MakeUnit("Target", 10f, 1), 10f,
                needed: 6)), Is.EqualTo(0f));
        }

        [TestCase(5, 0f)]
        [TestCase(6, 1f)]
        public async Task EnhancedBloodborn_FatiguedMelee_GetsNothingFromFivesOrSixes(int face, float expectedHits)
        {
            // Fatigue: 6s only, and those 6s "don't count as rolls of 6 for special rules" (#418) - so no extra hit from
            // either face. Through the real DetermineHitRollStage, which sets the 6+ threshold.
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedFaceDiceRoller(face));
            DataBinding<UnitData> berserkers = MakeUnit("Berserkers", 0f, 1, Bases.Bloodborn, Enhanced36Definitions.HitsOnFive());
            FatigueUtilities.ApplyFatigued(berserkers.GetValue());
            DataBinding<UnitData> target = MakeUnit("Target", 10f, 1);
            Place(target, 0.5f);

            var metadata = new CombatMetadata(_ctx, berserkers, target, new Weapon("Axe", 0f, 1, 0), weaponCount: 1,
                attackerMoved: false, isMelee: true, isCharging: true);
            var determine = new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            determine.NextStage.Bind("done");
            await determine.Enter(metadata);
            var roll = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            roll.NextStage.Bind("done");
            await roll.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults threshold), Is.True);
            Assert.That(threshold.HitRollNeeded, Is.EqualTo(6), "fatigued: 6s only");
            Assert.That(metadata.QueryForResult(out RollToHitResults hits), Is.True);
            Assert.That(TotalHits(hits), Is.EqualTo(expectedHits), face == 5 ? "the 5 missed" : "the 6 hits, once");
            Assert.That(HitsOfKind(hits, EHitSourceKind.ExtraHits), Is.Zero, "no extra hit from a 5 or a 6");
        }

        [Test]
        public async Task EnhancedBloodborn_WithoutBloodborn_IsInert()
        {
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedFaceDiceRoller(5));
            DataBinding<UnitData> pretenders = MakeUnit("Pretenders", 0f, 1, Enhanced36Definitions.HitsOnFive());

            Assert.That(TotalHits(await Hits(new Weapon("Gun", 24f, 1, 0), pretenders, MakeUnit("Target", 10f, 1), 10f)),
                Is.EqualTo(1f));
        }

        [Test]
        public async Task EnhancedBloodborn_HistogramMode_PricesTheFivesAtOneInSix()
        {
            // 6 dice at 4+: 3 hits; the base adds 1 (the 6s), the Enhanced arm adds 1 more (the 5s, all successful at 4+).
            DataBinding<UnitData> plain = MakeUnit("Berserkers", 0f, 1, Bases.Bloodborn);
            DataBinding<UnitData> enhanced = MakeUnit("Berserkers", 0f, 1, Bases.Bloodborn, Enhanced36Definitions.HitsOnFive());

            Assert.That(TotalHits(await Hits(new Weapon("Gun", 24f, 6, 0), plain, MakeUnit("Target", 10f, 1), 10f, attacks: 6)),
                Is.EqualTo(4f).Within(0.0001f));
            Assert.That(TotalHits(await Hits(new Weapon("Gun", 24f, 6, 0), enhanced, MakeUnit("Target", 10f, 1), 10f, attacks: 6)),
                Is.EqualTo(5f).Within(0.0001f));
            Assert.That(TotalHits(await Hits(new Weapon("Gun", 24f, 6, 0), enhanced, MakeUnit("Target", 10f, 1), 10f, attacks: 6,
                needed: 6)), Is.EqualTo(2f).Within(0.0001f), "needing 6s: 1 hit plus the base's extra; the 5s all missed");
        }

        // --- helpers --------------------------------------------------------------------------------------------------

        private static float HitsOfKind(RollToHitResults results, EHitSourceKind kind) =>
            results.SuccessfulHitList.Where(hit => hit.Source.Kind == kind).Sum(hit => hit.HitCount);

        private static int MoraleNet(TestRuleHarness harness, IUnit unit, PreMoraleTestContext context)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluate(unit, ERuleSeat.Actor, context));
            return sink.Net(ERollKind.Morale);
        }

        private static float TotalHits(RollToHitResults results) => results.SuccessfulHitList.Sum(h => h.HitCount);

        private async Task<int> HitNeeded(DataBinding<UnitData> defender, float distance, bool melee, bool charging,
            float origin, DataBinding<UnitData>? attacker = null)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1);
            Place(defender, distance);

            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, new Weapon("Gun", melee ? 0f : 24f, 1, 0),
                weaponCount: 1, attackerMoved: false, isMelee: melee, isCharging: charging,
                chargeOriginDistanceInches: origin);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            return result.HitRollNeeded;
        }

        private async Task<RollToHitResults> Hits(Weapon weapon, DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            float distance, bool melee = false, bool charging = false, float origin = 0f, bool isSpell = false,
            int needed = 4, int attacks = 1)
        {
            Place(defender, distance);

            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1, attackerMoved: false,
                isMelee: melee, isCharging: charging, isSpell: isSpell, chargeOriginDistanceInches: origin);
            metadata.AddResult(new DetermineHitRollResults(needed, attacks));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        // The movement machinery looks a moving unit up through its army.
        private DataBinding<UnitData> InArmy(DataBinding<UnitData> unit)
        {
            _store.Create(new ArmyData(unit.GetValue().PlayerID, new List<DataBinding<UnitData>> { unit }));
            return unit;
        }

        private async Task RunActivationStart(DataBinding<UnitData> unit, IPlayerRequestByID requester, IDiceRoller dice)
        {
            InArmy(unit);
            var ctx = new TriggeredMoveTestContext(_store, requester, dice);
            var unitContext = new UnitActionContext(ctx, unit);
            unitContext.Reset(unit);
            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(unitContext);
        }

        private async Task<int> SaveNeeded(Weapon weapon, DataBinding<UnitData> defender, float distance,
            DataBinding<UnitData>? attacker = null, bool melee = false, bool charging = false, float origin = 0f,
            bool isSpell = false)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1);
            RollToHitResults hits = await Hits(weapon, attacker, defender, distance, melee, charging, origin, isSpell);

            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result.PendingSaveRollsList.Max(p => p.SaveNeeded);
        }

        private async Task<float> WoundsFromOneFailedSave(DataBinding<UnitData> defender, float distance = 10f,
            bool melee = false, float origin = 0f, bool isSpell = false, Weapon? weapon = null,
            DataBinding<UnitData>? attacker = null, int failedFace = 2)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1);
            Place(defender, distance);

            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon ?? new Weapon("Gun", 24f, 1, 0),
                weaponCount: 1, attackerMoved: false, isMelee: melee, isCharging: melee && origin > 0f, isSpell: isSpell,
                chargeOriginDistanceInches: origin);
            var failed = new List<FailedSaveInfo>
            {
                new FailedSaveInfo(TestDice.Faces(failedFace), new PendingSaveRolls(TestDice.Faces(failedFace), 4)),
            };
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failed));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            return result.TotalWoundsToAssign;
        }

        private DataBinding<UnitData> MakeUnit(string name, float z, int models, params SpecialRuleDefinition[] rules)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(i * 1.5f, z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: bindings);
            foreach (SpecialRuleDefinition rule in rules) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static void Place(DataBinding<UnitData> unit, float edgeDistance)
        {
            List<DataBinding<ModelData>> models = unit.GetValue().ModelBindings;
            for (int i = 0; i < models.Count; i++)
            {
                models[i].GetValue().SetPosition(new Position(i * 1.5f, edgeDistance + 2f * BaseRadius));
            }
        }

        // Records every movement-path request (its budget) and declines it; answers any Yes/No with yes.
        private sealed class CapturingMoveRequester : IPlayerRequestByID
        {
            public int Asked;
            public DefineMovementPathRequest? Captured;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case YesNoRequest:
                        return Task.FromResult((TReply)(object)true);
                    case DefineMovementPathRequest move:
                        Asked++;
                        Captured = move;
                        return Task.FromResult((TReply)(object)new Cancelled<List<ModelMoveEntry>>());
                    default:
                        throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
                }
            }
        }
    }
}
