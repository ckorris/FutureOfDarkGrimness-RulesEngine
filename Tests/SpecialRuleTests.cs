using System;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Phase 6 red baseline for Work Item #042. Each test constructs a rule inline,
    // attaches it to the relevant unit, fires the matching hook context, and asserts
    // on the RuleOperation queue the bus should produce. They FAIL today: RuleHookBus
    // is still the Phase 4 stub returning an empty queue. Phase 7 turns them green in
    // the order documented in 042-implementation-checklist.txt.
    [TestFixture]
    public class SpecialRuleTests
    {
        private static readonly ActivatedAbility[] NoAbilities = Array.Empty<ActivatedAbility>();

        // Sanity check on the harness wiring itself, independent of any rule.
        [Test]
        public void HarnessFires_NoRules_ReturnsEmpty()
        {
            var harness = new TestRuleHarness();

            var operations = harness.Fire(new TestHookContext(EHookID.Round_OnRoundStart));

            Assert.That(operations, Is.Empty);
        }

        // 01 — Furious: each unmodified 6 to hit generates an extra hit.
        [Test]
        public void Furious_OnUnmodified6Hit_AddsExtraHit()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Furious",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.UnmodifiedRollEquals(6),
                        new Effect.AddExtraHit(OnRollValue: 6),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Furious");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollCompleteContext(attacker, target,
                UnmodifiedHitRolls: new[] { 6, 3, 6 }));

            ops.HasOperation<RuleOperation.InsertExtraHits>();
        }

        // 02 — Stealth: when shot from beyond 9", hit rolls suffer -1.
        [Test]
        public void Stealth_WhenShotFromBeyond9_AppliesMinus1ToHit()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Stealth",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.DistanceGreaterThan(9f),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack,
                        ERuleSeat.Subject),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3);
            IUnit defender = harness.BuildUnit("P2", modelCount: 5, "Stealth");

            var ops = harness.Evaluate(defender, ERuleSeat.Subject,
                new HitRollModifierContext(attacker, defender, DistanceInches: 12f));

            ops.HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Hit && op.Delta == -1);
        }

        // 02b — Stealth is a SUBJECT-seat rule: it must NOT fire when the unit carrying
        // it is the attacker. Stealth is defensive; a shooter with Stealth gains nothing.
        [Test]
        public void Stealth_OnAttacker_DoesNotApplyModifier()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Stealth",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.DistanceGreaterThan(9f),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack,
                        ERuleSeat.Subject),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Stealth");
            IUnit defender = harness.BuildUnit("P2", modelCount: 5);

            // The attacker plays the Actor seat; Stealth only fires from Subject, so the
            // seat mismatch must yield no operation — even though the distance condition
            // would otherwise pass.
            var ops = harness.Evaluate(attacker, ERuleSeat.Actor,
                new HitRollModifierContext(attacker, defender, DistanceInches: 12f));

            Assert.That(ops, Is.Empty);
        }

        // 03 — Fast: +2" when the declared action is Advance.
        [Test]
        public void Fast_OnAdvance_AddsTwoInches()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Fast",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Advance),
                        new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                        ELifetime.ThisActivation),
                },
                NoAbilities));

            IUnit unit = harness.BuildUnit("P1", modelCount: 3, "Fast");

            var ops = harness.Fire(new MoveActionDeclaredContext(unit, EActionType.Advance, BaseDistanceInches: 6f));

            ops.HasOperation<RuleOperation.ApplyMovementBonus>(
                op => op.ActionType == EActionType.Advance && op.DistanceInches == 2f);
        }

        // 04 — Melee Shrouding: an enemy charging the bearer loses 3" of charge movement.
        [Test]
        public void MeleeShrouding_OnEnemyCharge_AppliesMinus3MovementToEnemy()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Melee Shrouding",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnChargeDeclared,
                        new Condition.Always(),
                        new Effect.MovementBonus(EActionType.Charge, DistanceInches: -3f),
                        ELifetime.ThisActivation),
                },
                NoAbilities));

            IUnit charger = harness.BuildUnit("P1", modelCount: 3);
            IUnit defender = harness.BuildUnit("P2", modelCount: 5, "Melee Shrouding");

            var ops = harness.Fire(new ChargeDeclaredContext(charger, defender, BaseDistanceInches: 12f));

            ops.HasOperation<RuleOperation.ApplyMovementBonus>(
                op => op.ActionType == EActionType.Charge && op.DistanceInches == -3f);
        }

        // 05 — Bane: the defender must re-roll unmodified Defense 6s.
        [Test]
        public void Bane_OnDefenseUnmodified6_TriggersReroll()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Bane",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                        new Condition.Always(),
                        new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue()),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Bane");
            IUnit defender = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new SaveRollCompleteContext(attacker, defender,
                UnmodifiedSaveRolls: new[] { 6, 2, 6 }));

            ops.HasOperation<RuleOperation.ApplyReroll>(op => op.Roll == ERollKind.Save);
        }

        // 06 — Rending: an unmodified 6 to hit promotes the attack to AP4
        //      (modelled as a -4 modifier to the defender's save roll).
        [Test]
        public void Rending_OnHitRoll6_PromotesToAP4()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Rending",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.UnmodifiedRollEquals(6),
                        new Effect.RollModifier(ERollKind.Save, Delta: -4),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Rending");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollCompleteContext(attacker, target,
                UnmodifiedHitRolls: new[] { 6, 4, 2 }));

            ops.HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Save && op.Delta == -4);
        }

        // 07 — Mend: at OnPreAttack the caster is offered the ability, carrying its cost.
        [Test]
        public void Mend_OnPreAttack_OffersAbilityWithCost()
        {
            var harness = new TestRuleHarness();
            var mend = new ActivatedAbility(
                TriggerHook: EHookID.Activation_OnPreAttack,
                Cost: new Cost.SpellTokens(1),
                TargetSelector: new TargetSelector(RangeInches: 6f, MinCount: 1, MaxCount: 1,
                    ETargetAffinity.Friend, RequireLineOfSight: false),
                Effect: new Effect.Heal(new DiceExpression.D3()),
                AvailableWhen: new Condition.Always());
            harness.Register(new SpecialRuleDefinition("Mend", Array.Empty<HookEntry>(), new[] { mend }));

            IUnit caster = harness.BuildUnit("P1", modelCount: 1, "Mend");
            harness.SeedToken(caster, TokenType.SpellTokens, count: 2);

            var offers = harness.OfferAbilities(new PreAttackContext(caster, EActionType.Hold));

            offers.HasOffer("Mend", o => o.Ability.Cost is Cost.SpellTokens);
        }

        // 08 — Mend: when accepted against a friendly, it heals a target model.
        [Test]
        public void Mend_WhenAccepted_HealsTargetModel()
        {
            var harness = new TestRuleHarness();
            var mend = new ActivatedAbility(EHookID.Activation_OnPreAttack, new Cost.SpellTokens(1),
                new TargetSelector(6f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.Heal(new DiceExpression.D3()), new Condition.Always());
            harness.Register(new SpecialRuleDefinition("Mend", Array.Empty<HookEntry>(), new[] { mend }));

            IUnit caster = harness.BuildUnit("P1", modelCount: 1, "Mend");
            IUnit ally = harness.BuildUnit("P1", modelCount: 3);

            var ops = harness.Accept(new AbilityOffer(caster, "Mend", mend), ally);

            ops.HasOperation<RuleOperation.InvokeHeal>();
        }

        // 09 — Advanced Sight (spell): when cast, grants a "next time" buff to targets.
        [Test]
        public void SpellAdvancedSight_WhenCast_AppliesNextTimeBuffToTargets()
        {
            var harness = new TestRuleHarness();
            var spell = new ActivatedAbility(EHookID.Activation_OnPreAttack, new Cost.SpellTokens(2),
                new TargetSelector(12f, 1, 2, ETargetAffinity.Friend, false),
                new Effect.AddRule("Advanced Sight", ELifetime.NextTrigger), new Condition.Always());
            harness.Register(new SpecialRuleDefinition("Advanced Sight", Array.Empty<HookEntry>(), new[] { spell }));

            IUnit caster = harness.BuildUnit("P1", modelCount: 1, "Advanced Sight");
            IUnit ally = harness.BuildUnit("P1", modelCount: 3);

            var ops = harness.Accept(new AbilityOffer(caster, "Advanced Sight", spell), ally);

            ops.HasOperation<RuleOperation.GrantTokenToUnit>(op =>
                op.TokenToGrant.Payload is TokenPayload.RuleGrant rg && rg.RuleName == "Advanced Sight");
        }

        // 10 — Piercing Frenzy: destroying an enemy unit grants the bearer a marker token.
        [Test]
        public void PiercingFrenzy_OnEnemyUnitDestroyed_AddsMarkerToken()
        {
            var harness = new TestRuleHarness();
            var marker = new TokenType("PiercingFrenzy");
            harness.Register(new SpecialRuleDefinition("Piercing Frenzy",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnUnitDestroyed,
                        new Condition.Always(),
                        new Effect.GrantToken(marker, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                        ELifetime.UntilEndOfGame),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Piercing Frenzy");
            IUnit enemy = harness.BuildUnit("P2", modelCount: 1);

            var ops = harness.Fire(new UnitDestroyedContext(DestroyedUnit: enemy, KillerUnit: attacker));

            ops.HasOperation<RuleOperation.GrantTokenToUnit>(
                op => op.Unit == attacker && op.TokenToGrant.Type == marker);
        }

        // 11 — Piercing Frenzy: with two markers, attacks gain AP+2 (save modifier -2).
        [Test]
        public void PiercingFrenzy_WithTwoMarkers_AddsAPPlus2InRolls()
        {
            var harness = new TestRuleHarness();
            var marker = new TokenType("PiercingFrenzy");
            harness.Register(new SpecialRuleDefinition("Piercing Frenzy",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.TokenPresent(marker, MinCount: 2),
                        new Effect.RollModifier(ERollKind.Save, Delta: -2),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Piercing Frenzy");
            harness.SeedToken(attacker, marker, count: 2);
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollCompleteContext(attacker, target, new[] { 4, 4, 4 }));

            ops.HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Save && op.Delta == -2);
        }

        // 12 — Regeneration Aura: grants Regeneration to the bearer's whole unit.
        [Test]
        public void RegenerationAura_AppliesRegenerationToWholeUnit()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Regeneration Aura",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                        new Condition.Always(),
                        new Effect.Aura("Regeneration"),
                        ELifetime.Aura),
                },
                NoAbilities));

            IUnit unit = harness.BuildUnit("P1", modelCount: 4, "Regeneration Aura");

            var ops = harness.Fire(new UnitCreatedContext(unit));

            ops.HasOperation<RuleOperation.GrantTokenToUnit>(op => op.Unit == unit &&
                op.TokenToGrant.Payload is TokenPayload.RuleGrant rg && rg.RuleName == "Regeneration");
        }

        // 15 — Unstoppable Mark: places a token on an enemy, owned by the bearer.
        [Test]
        public void UnstoppableMark_PlacesTokenOnEnemy_WithOwnerSet()
        {
            var harness = new TestRuleHarness();
            var markType = new TokenType("UnstoppableMark");
            var ability = new ActivatedAbility(EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                new Effect.GrantToken(markType, new ValueSource.Literal(1), new TokenClearTrigger.OwnerDestroyed()), new Condition.Always());
            harness.Register(new SpecialRuleDefinition("Unstoppable Mark", Array.Empty<HookEntry>(), new[] { ability }));

            IUnit bearer = harness.BuildUnit("P1", modelCount: 3, "Unstoppable Mark");
            IUnit enemy = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Accept(new AbilityOffer(bearer, "Unstoppable Mark", ability), enemy);

            ops.HasOperation<RuleOperation.GrantTokenToUnit>(op => op.Unit == enemy &&
                op.TokenToGrant.Type == markType && op.TokenToGrant.OwnerUnitID == bearer.ID);
        }

        // 16 — Unstoppable Mark: the mark is cleared when its owner (placer) is destroyed.
        [Test]
        public void UnstoppableMark_WhenOwnerDestroyed_TokenRemoved()
        {
            var harness = new TestRuleHarness();
            var markType = new TokenType("UnstoppableMark");

            IUnit bearer = harness.BuildUnit("P1", modelCount: 3);
            IUnit enemy = harness.BuildUnit("P2", modelCount: 5);
            harness.SeedToken(enemy, markType, count: 1, owner: bearer.ID,
                clear: new TokenClearTrigger.OwnerDestroyed());

            harness.Fire(new UnitDestroyedContext(DestroyedUnit: bearer, KillerUnit: enemy));

            Assert.That(enemy.Tokens.HasToken(markType), Is.False,
                "An owner-destroyed mark should be cleared when its placing unit dies.");
        }

        // 17 — Vanguard: after deploying, offers an optional reposition of up to 9".
        [Test]
        public void Vanguard_AfterDeploy_OffersReposition_Within9Inches()
        {
            var harness = new TestRuleHarness();
            var ability = new ActivatedAbility(EHookID.Deployment_OnUnitDeployed, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.TriggeredMove(MaxInches: 9f, IsOptional: true), new Condition.Always());
            harness.Register(new SpecialRuleDefinition("Vanguard", Array.Empty<HookEntry>(), new[] { ability }));

            IUnit unit = harness.BuildUnit("P1", modelCount: 3, "Vanguard");

            var ops = harness.Accept(new AbilityOffer(unit, "Vanguard", ability), unit);

            ops.HasOperation<RuleOperation.InvokeTriggeredMove>(op => op.Unit == unit && op.MaxInches == 9f);
        }

        // 18 — Martial Prowess: offers a once-per-game reactivation of the bearer.
        [Test]
        public void MartialProwess_OnNextActivator_OffersReactivation_OncePerGame()
        {
            var harness = new TestRuleHarness();
            var ability = new ActivatedAbility(EHookID.Activation_OnNextActivatorRequested, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.Reactivate(), new Condition.Always());
            harness.Register(new SpecialRuleDefinition("Martial Prowess", Array.Empty<HookEntry>(), new[] { ability }));

            IUnit unit = harness.BuildUnit("P1", modelCount: 3, "Martial Prowess");

            Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>());

            var ops = harness.Accept(new AbilityOffer(unit, "Martial Prowess", ability), unit);

            ops.HasOperation<RuleOperation.InvokeReactivate>(op => op.Unit == unit);
        }

        // 19 — Strafing: moving through an enemy offers a mid-move attack on that enemy.
        [Test]
        public void Strafing_DuringMoveThroughEnemy_OffersMidMoveAttack()
        {
            var harness = new TestRuleHarness();
            var ability = new ActivatedAbility(EHookID.Movement_OnMoveThroughEnemy, new Cost.OncePerActivation(),
                new TargetSelector(1f, 1, 1, ETargetAffinity.Foe, false),
                new Effect.DealHits(Count: 3, WithRules: Array.Empty<string>()), new Condition.Always());
            harness.Register(new SpecialRuleDefinition("Strafing", Array.Empty<HookEntry>(), new[] { ability }));

            IUnit mover = harness.BuildUnit("P1", modelCount: 3, "Strafing");
            IUnit enemy = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Accept(new AbilityOffer(mover, "Strafing", ability), enemy);

            ops.HasOperation<RuleOperation.InvokeDealHits>(op => op.Target == enemy && op.Count == 3);
        }

        // Deadly(X) — core rule, argument-bearing. Each wound is multiplied by the
        // rule's argument. Validates the argument model end-to-end: the X=3 lives on
        // the attachment (RuleArgument), and the effect reads it via ValueSource.Arg(0).
        [Test]
        public void Deadly_OnWound_MultipliesByArgument()
        {
            var harness = new TestRuleHarness();
            var deadly = new SpecialRuleDefinition("Deadly",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnPreApplyWound,
                        new Condition.Always(),
                        new Effect.MultiplyWounds(new ValueSource.Arg(0)),
                        ELifetime.ThisAttack),
                },
                NoAbilities);
            harness.Register(deadly);

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3);
            harness.AttachRule(attacker, deadly, new RuleArgument.Int(3));
            IUnit defender = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new PreApplyWoundContext(attacker, defender));

            ops.HasOperation<RuleOperation.MultiplyWounds>(op => op.Multiplier == 3);
        }

        // Surge (core) — unmodified 6 to hit deals 1 extra hit (any shooting).
        [Test]
        public void Surge_OnUnmodified6Hit_AddsExtraHit()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Surge",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.UnmodifiedRollEquals(6),
                        new Effect.AddExtraHit(OnRollValue: 6),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Surge");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollCompleteContext(attacker, target, new[] { 6, 2, 6 }));

            ops.HasOperation<RuleOperation.InsertExtraHits>();
        }

        // Relentless (core) — when shooting from beyond 9", unmodified 6s add a hit.
        [Test]
        public void Relentless_WhenShootingBeyond9_OnUnmodified6_AddsExtraHit()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Relentless",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.And(new Condition.UnmodifiedRollEquals(6),
                                          new Condition.DistanceGreaterThan(9f)),
                        new Effect.AddExtraHit(OnRollValue: 6),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Relentless");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollCompleteContext(attacker, target,
                new[] { 6, 4, 6 }, DistanceInches: 12f));

            ops.HasOperation<RuleOperation.InsertExtraHits>();
        }

        // Slow (core) — -2" when advancing (also -4" on Rush/Charge).
        [Test]
        public void Slow_OnAdvance_SubtractsTwoInches()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Slow",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Advance),
                        new Effect.MovementBonus(EActionType.Advance, DistanceInches: -2f),
                        ELifetime.ThisActivation),
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Rush),
                        new Effect.MovementBonus(EActionType.Rush, DistanceInches: -4f),
                        ELifetime.ThisActivation),
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Charge),
                        new Effect.MovementBonus(EActionType.Charge, DistanceInches: -4f),
                        ELifetime.ThisActivation),
                },
                NoAbilities));

            IUnit unit = harness.BuildUnit("P1", modelCount: 3, "Slow");

            var ops = harness.Fire(new MoveActionDeclaredContext(unit, EActionType.Advance, BaseDistanceInches: 6f));

            ops.HasOperation<RuleOperation.ApplyMovementBonus>(
                op => op.ActionType == EActionType.Advance && op.DistanceInches == -2f);
        }

        // Thrust (core) — when charging, +1 to hit and AP(+1) (modelled as save -1).
        [Test]
        public void Thrust_OnCharge_AddsHitAndAP()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Thrust",
                new[]
                {
                    new HookEntry(EHookID.Melee_OnChargeContact, new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Hit, Delta: +1), ELifetime.ThisAttack),
                    new HookEntry(EHookID.Melee_OnChargeContact, new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Save, Delta: -1), ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Thrust");
            IUnit defender = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new ChargeContactContext(attacker, defender));

            ops.HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Hit && op.Delta == +1);
            ops.HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Save && op.Delta == -1);
        }

        // Indirect (core) — -1 to hit when shooting after moving.
        [Test]
        public void Indirect_WhenShootingAfterMoving_AppliesMinus1ToHit()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Indirect",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.AfterMoving(),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Indirect");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollModifierContext(attacker, target,
                DistanceInches: 10f, AttackerMoved: true));

            ops.HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Hit && op.Delta == -1);
        }

        // Reliable (core) — attacks at Quality 2+.
        [Test]
        public void Reliable_AttacksAtQuality2Plus()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Reliable",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.Always(),
                        new Effect.QualityFloor(Quality: 2),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Reliable");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollModifierContext(attacker, target, DistanceInches: 6f));

            ops.HasOperation<RuleOperation.QualityFloor>(op => op.Quality == 2);
        }

        // Unstoppable (core) — ignores Regeneration (and all negative modifiers).
        [Test]
        public void Unstoppable_IgnoresRegeneration()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Unstoppable",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                        new Condition.Always(),
                        new Effect.IgnoreRule("Regeneration"),
                        ELifetime.ThisAttack),
                    // Second facet — "ignores all negative modifiers to this weapon" —
                    // needs a new Effect.IgnoreNegativeModifiers at a modifier hook;
                    // added when we model modifier immunity.
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3, "Unstoppable");
            IUnit defender = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new SaveRollCompleteContext(attacker, defender, new[] { 3, 4 }));

            ops.HasOperation<RuleOperation.SuppressRule>(op => op.RuleName == "Regeneration");
        }

        // Fearless (core) — failed morale gets a second chance.
        [Test]
        public void Fearless_OnFailedMorale_TriggersReroll()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Fearless",
                new[]
                {
                    new HookEntry(EHookID.Morale_OnMoraleTestComplete,
                        new Condition.Always(),
                        new Effect.Reroll(ERollKind.Morale, new RerollCondition.AllFailures()),
                        ELifetime.ThisActivation),
                },
                NoAbilities));
            // Rulebook: on failed morale, roll again and 4+ counts as passed. Modelled
            // as a morale reroll; the fixed-4+ threshold is an execution detail (Phase 8).

            IUnit unit = harness.BuildUnit("P1", modelCount: 5, "Fearless");

            var ops = harness.Fire(new MoraleTestContext(unit));

            ops.HasOperation<RuleOperation.ApplyReroll>(op => op.Roll == ERollKind.Morale);
        }

        // Regeneration (core) — each wound is ignored on a 5+.
        [Test]
        public void Regeneration_OnWound_IgnoredOn5Plus()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Regeneration",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                        new Condition.Always(),
                        new Effect.IgnoreWoundOnRoll(MinRoll: 5),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3);
            IUnit defender = harness.BuildUnit("P2", modelCount: 5, "Regeneration");

            var ops = harness.Fire(new SaveRollCompleteContext(attacker, defender, new[] { 2, 3 }));

            ops.HasOperation<RuleOperation.IgnoreWound>(op => op.MinRoll == 5);
        }

        // Tough(X) (core) — sets the model's max wounds to its argument at creation.
        [Test]
        public void Tough_OnCreation_SetsMaxWoundsToArgument()
        {
            var harness = new TestRuleHarness();
            var tough = new SpecialRuleDefinition("Tough",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                        new Condition.Always(),
                        new Effect.SetMaxWounds(new ValueSource.Arg(0)),
                        ELifetime.UntilEndOfGame),
                },
                NoAbilities);
            harness.Register(tough);

            IUnit unit = harness.BuildUnit("P1", modelCount: 1);
            harness.AttachRule(unit, tough, new RuleArgument.Int(6));

            var ops = harness.Fire(new UnitCreatedContext(unit));

            ops.HasOperation<RuleOperation.SetMaxWounds>(op => op.MaxWounds == 6);
        }

        // Blast(X) (core) — multiplies each hit by its argument.
        [Test]
        public void Blast_OnHit_MultipliesHitsByArgument()
        {
            var harness = new TestRuleHarness();
            var blast = new SpecialRuleDefinition("Blast",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.Always(),
                        new Effect.MultiplyHits(new ValueSource.Arg(0)),
                        ELifetime.ThisAttack),
                },
                NoAbilities);
            harness.Register(blast);

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3);
            harness.AttachRule(attacker, blast, new RuleArgument.Int(3));
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollCompleteContext(attacker, target, new[] { 5 }));

            ops.HasOperation<RuleOperation.MultiplyHits>(op => op.Multiplier == 3);
        }

        // Impact(X) (core) — rolls X impact dice on the charge.
        [Test]
        public void Impact_OnCharge_RollsArgumentImpactDice()
        {
            var harness = new TestRuleHarness();
            var impact = new SpecialRuleDefinition("Impact",
                new[]
                {
                    new HookEntry(EHookID.Melee_OnChargeContact,
                        new Condition.Always(),
                        new Effect.ChargeImpactHits(new ValueSource.Arg(0)),
                        ELifetime.ThisAttack),
                },
                NoAbilities);
            harness.Register(impact);

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3);
            harness.AttachRule(attacker, impact, new RuleArgument.Int(2));
            IUnit defender = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new ChargeContactContext(attacker, defender));

            ops.HasOperation<RuleOperation.ChargeImpactHits>(op => op.DiceCount == 2);
        }

        // Fear(X) (core) — counts as +X wounds when deciding who won melee.
        [Test]
        public void Fear_OnMeleeResolution_AddsArgumentToWoundCount()
        {
            var harness = new TestRuleHarness();
            var fear = new SpecialRuleDefinition("Fear",
                new[]
                {
                    new HookEntry(EHookID.Melee_OnMeleeResolution,
                        new Condition.Always(),
                        new Effect.ExtraMeleeWoundCount(new ValueSource.Arg(0)),
                        ELifetime.ThisAttack),
                },
                NoAbilities);
            harness.Register(fear);

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3);
            harness.AttachRule(attacker, fear, new RuleArgument.Int(2));
            IUnit defender = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new MeleeResolutionContext(attacker, defender));

            ops.HasOperation<RuleOperation.ExtraMeleeWoundCount>(op => op.Amount == 2);
        }

        // Caster(X) (core) — gains X spell tokens at the start of each round.
        // Arg-driven token grant: count comes from the rule's argument via the
        // GrantToken effect's ValueSource. The 6-token cap is execution (Phase 8).
        [Test]
        public void Caster_OnRoundStart_GrantsArgumentSpellTokens()
        {
            var harness = new TestRuleHarness();
            var caster = new SpecialRuleDefinition("Caster",
                new[]
                {
                    new HookEntry(EHookID.Round_OnRoundStart,
                        new Condition.Always(),
                        new Effect.GrantToken(TokenType.SpellTokens, new ValueSource.Arg(0),
                            new TokenClearTrigger.ManualOnly()),
                        ELifetime.UntilEndOfGame),
                },
                NoAbilities);
            harness.Register(caster);

            IUnit unit = harness.BuildUnit("P1", modelCount: 1);
            harness.AttachRule(unit, caster, new RuleArgument.Int(2));

            var ops = harness.Fire(new RoundStartContext(unit));

            ops.HasOperation<RuleOperation.GrantTokenToUnit>(op => op.Unit == unit &&
                op.TokenToGrant.Type == TokenType.SpellTokens && op.TokenToGrant.Count == 2);
        }

        // Artillery (core) — +1 to hit when shooting at enemies over 9" away. Reuses
        // the roll-modifier vocabulary. The other facets — enemies get -2 to hit from
        // over 9", and the unit may only Hold — are separate entries (RangeModifier /
        // RestrictActions) added when modelled.
        [Test]
        public void Artillery_WhenShootingBeyond9_AddsOneToHit()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Artillery",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.DistanceGreaterThan(9f),
                        new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 1, "Artillery");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new HitRollModifierContext(attacker, target, DistanceInches: 12f));

            ops.HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Hit && op.Delta == +1);
        }

        // Limited (core) — the weapon may only be used once per game. Modelled at the
        // queue level as marking itself "used" after shooting; the gate that suppresses
        // a second use reads that marker (Phase 8).
        [Test]
        public void Limited_AfterShooting_MarksWeaponUsed()
        {
            var harness = new TestRuleHarness();
            var usedMarker = new TokenType("LimitedUsed");
            harness.Register(new SpecialRuleDefinition("Limited",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnPostShoot,
                        new Condition.Always(),
                        new Effect.GrantToken(usedMarker, new ValueSource.Literal(1),
                            new TokenClearTrigger.ManualOnly()),
                        ELifetime.UntilEndOfGame),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 1, "Limited");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new PostShootContext(attacker, target));

            ops.HasOperation<RuleOperation.GrantTokenToUnit>(
                op => op.Unit == attacker && op.TokenToGrant.Type == usedMarker);
        }

        // Counter (core) — a charged bearer strikes first. The companion facet (the
        // charger loses 1 Impact roll per Counter model) is an Impact-count modifier
        // added when that interaction is modelled.
        [Test]
        public void Counter_WhenCharged_StrikesFirst()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Counter",
                new[]
                {
                    new HookEntry(EHookID.Melee_OnCounterTrigger,
                        new Condition.Always(),
                        new Effect.StrikeFirst(),
                        ELifetime.ThisActivation),
                },
                NoAbilities));

            IUnit charger = harness.BuildUnit("P1", modelCount: 3);
            IUnit defender = harness.BuildUnit("P2", modelCount: 5, "Counter");

            var ops = harness.Fire(new CounterTriggerContext(charger, defender));

            ops.HasOperation<RuleOperation.StrikeFirst>();
        }

        // Takedown (core) — may pick one model in the target unit, resolved as a unit
        // of one. The "resolved before other weapons" ordering is a dispatch detail.
        [Test]
        public void Takedown_OnTargetsSelected_TargetsIndividualModel()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Takedown",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnShootTargetsSelected,
                        new Condition.Always(),
                        new Effect.TargetIndividualModel(),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 1, "Takedown");
            IUnit target = harness.BuildUnit("P2", modelCount: 5);

            var ops = harness.Fire(new ShootTargetsSelectedContext(attacker, target));

            ops.HasOperation<RuleOperation.TargetIndividualModel>();
        }

        // Immobile (core) — may only use Hold actions. Fires when a non-Hold action is
        // declared and restricts the choice set back to Hold.
        [Test]
        public void Immobile_OnNonHoldAction_RestrictsToHold()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Immobile",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.Not(new Condition.ActionTypeIs(EActionType.Hold)),
                        new Effect.RestrictActions(new[] { EActionType.Hold }),
                        ELifetime.ThisActivation),
                },
                NoAbilities));

            IUnit unit = harness.BuildUnit("P1", modelCount: 1, "Immobile");

            var ops = harness.Fire(new MoveActionDeclaredContext(unit, EActionType.Advance, BaseDistanceInches: 6f));

            ops.HasOperation<RuleOperation.RestrictActions>(
                op => op.Allowed.Count == 1 && op.Allowed[0] == EActionType.Hold);
        }

        // Aircraft (core) — units targeting it get -12" range. (Movement constraints —
        // Advance-only, +30" straight line — are separate RestrictActions / movement
        // entries added with the engine refactor.)
        [Test]
        public void Aircraft_WhenTargeted_AppliesMinus12Range()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Aircraft",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.Always(),
                        new Effect.RangeModifier(Delta: -12),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            IUnit attacker = harness.BuildUnit("P1", modelCount: 3);
            IUnit aircraft = harness.BuildUnit("P2", modelCount: 1, "Aircraft");

            var ops = harness.Fire(new HitRollModifierContext(attacker, aircraft, DistanceInches: 20f));

            ops.HasOperation<RuleOperation.ApplyRangeModifier>(op => op.Delta == -12);
        }

        // Flying (core) — moves through units and terrain, ignoring terrain effects.
        // The through-units facet is a separate movement-permission flag (Phase 8).
        [Test]
        public void Flying_OnMove_IgnoresTerrainEffects()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Flying",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.Always(),
                        new Effect.IgnoreTerrainEffects(),
                        ELifetime.ThisActivation),
                },
                NoAbilities));

            IUnit unit = harness.BuildUnit("P1", modelCount: 3, "Flying");

            var ops = harness.Fire(new MoveActionDeclaredContext(unit, EActionType.Advance, BaseDistanceInches: 6f));

            ops.HasOperation<RuleOperation.IgnoreTerrainEffects>();
        }

        // Strider (core) — ignores difficult-terrain effects when moving. Same queued
        // operation as Flying; the difficult-only vs all-terrain distinction is
        // execution (Phase 8).
        [Test]
        public void Strider_OnMove_IgnoresTerrainEffects()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Strider",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.Always(),
                        new Effect.IgnoreTerrainEffects(),
                        ELifetime.ThisActivation),
                },
                NoAbilities));

            IUnit unit = harness.BuildUnit("P1", modelCount: 3, "Strider");

            var ops = harness.Fire(new MoveActionDeclaredContext(unit, EActionType.Advance, BaseDistanceInches: 6f));

            ops.HasOperation<RuleOperation.IgnoreTerrainEffects>();
        }

        // Scout (core) — set aside, then deployed after others within 12" of the zone.
        // Queue level asserts only that it defers deployment; placement is Phase 8.
        [Test]
        public void Scout_OnPreDeployment_DefersDeployment()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Scout",
                new[]
                {
                    new HookEntry(EHookID.Deployment_OnPreDeploymentSelect,
                        new Condition.Always(),
                        new Effect.DeferDeployment(),
                        ELifetime.UntilEndOfGame),
                },
                NoAbilities));

            IUnit unit = harness.BuildUnit("P1", modelCount: 3, "Scout");

            var ops = harness.Fire(new PreDeploymentSelectContext(unit));

            ops.HasOperation<RuleOperation.DeferDeployment>();
        }

        // Ambush (core) — set aside, then deployed a later round >9" from enemies.
        // Same deferral operation as Scout; the round timing is Phase 8.
        [Test]
        public void Ambush_OnPreDeployment_DefersDeployment()
        {
            var harness = new TestRuleHarness();
            harness.Register(new SpecialRuleDefinition("Ambush",
                new[]
                {
                    new HookEntry(EHookID.Deployment_OnPreDeploymentSelect,
                        new Condition.Always(),
                        new Effect.DeferDeployment(),
                        ELifetime.UntilEndOfGame),
                },
                NoAbilities));

            IUnit unit = harness.BuildUnit("P1", modelCount: 3, "Ambush");

            var ops = harness.Fire(new PreDeploymentSelectContext(unit));

            ops.HasOperation<RuleOperation.DeferDeployment>();
        }
    }
}
