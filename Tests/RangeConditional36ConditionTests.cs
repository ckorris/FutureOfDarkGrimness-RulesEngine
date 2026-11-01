using System.Linq;
using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #432 - the two conditions of the OPR 3.6 range-conditional family, against hand-built contexts: what each
    // attack context reports through IHasShootChargeDistance, the strict "over" edges, the separate 9"/6" arms, the
    // exclusions (a strike-back is not a charge, spell damage is not shooting), the morale context's trigger, the
    // JSON kinds, and the range family's new charge half on the effect / op. The stage plumbing that fills these
    // contexts is pinned by the *RuleIntegrationTests next to this file.
    [TestFixture]
    public class RangeConditional36ConditionTests
    {
        private TestRuleHarness _harness = null!;
        private IUnit _attacker = null!;
        private IUnit _target = null!;

        [SetUp]
        public void SetUp()
        {
            _harness = new TestRuleHarness();
            _attacker = _harness.BuildUnit("P1", 1);
            _target = _harness.BuildUnit("P2", 1);
        }

        private static bool Holds(Condition condition, IHookContext context, IUnit bearer) =>
            condition.Evaluate(new RuleInvocation(context, bearer, System.Array.Empty<RuleArgument>()));

        private HitRollModifierContext Shot(float inches) => new(_attacker, _target, inches);

        private HitRollModifierContext Charge(float fromInches) => new(_attacker, _target, DistanceInches: 0.5f,
            IsMelee: true, IsCharging: true, ChargeOriginDistanceInches: fromInches);

        [TestCase(10f, true)]
        [TestCase(9.01f, true)]
        [TestCase(9f, false)]
        [TestCase(8f, false)]
        public void Shooting_IsOverNineInches_Strictly(float inches, bool expected)
        {
            Assert.That(Holds(RangeConditional36Definitions.Attacking, Shot(inches), _attacker), Is.EqualTo(expected));
            Assert.That(Holds(RangeConditional36Definitions.Attacked, Shot(inches), _target), Is.EqualTo(expected),
                "the defender's mirror reads the same attack");
        }

        [TestCase(7f, true)]
        [TestCase(6.01f, true)]
        [TestCase(6f, false)]
        [TestCase(5f, false)]
        public void Charging_IsOverSixInches_Strictly(float fromInches, bool expected)
        {
            Assert.That(Holds(RangeConditional36Definitions.Attacking, Charge(fromInches), _attacker),
                Is.EqualTo(expected));
            Assert.That(Holds(RangeConditional36Definitions.Attacked, Charge(fromInches), _target),
                Is.EqualTo(expected));
        }

        [Test]
        public void TheTwoArms_HaveTheirOwnThresholds()
        {
            // A 7" charge passes the 6" arm; a 7" shot does not pass the 9" arm.
            Assert.That(Holds(RangeConditional36Definitions.Attacking, Charge(7f), _attacker), Is.True);
            Assert.That(Holds(RangeConditional36Definitions.Attacking, Shot(7f), _attacker), Is.False);

            // The pair is data: a hypothetical 12"/3" rule reads its own numbers.
            var other = new Condition.AttackingFromOver(ShootInches: 12f, ChargeInches: 3f);
            Assert.That(Holds(other, Shot(10f), _attacker), Is.False);
            Assert.That(Holds(other, Charge(4f), _attacker), Is.True);
        }

        [Test]
        public void AStrikeBack_IsNotACharge_WhateverTheOrigin()
        {
            var strikeBack = new HitRollModifierContext(_attacker, _target, DistanceInches: 0.5f, IsMelee: true,
                IsCharging: false, ChargeOriginDistanceInches: 20f);

            Assert.That(strikeBack.ChargeDistanceInches, Is.Null);
            Assert.That(strikeBack.ShootingDistanceInches, Is.Null, "a melee swing is not a shot either");
            Assert.That(Holds(RangeConditional36Definitions.Attacking, strikeBack, _attacker), Is.False);
            Assert.That(Holds(RangeConditional36Definitions.Attacked, strikeBack, _target), Is.False);
        }

        [Test]
        public void SpellDamage_IsNotShooting()
        {
            var spellHits = new HitRollCompleteContext(_attacker, _target, TestDice.Faces(6), DistanceInches: 15f,
                IsSpell: true);
            var spellSaves = new SaveRollCompleteContext(_attacker, _target, TestDice.Faces(1), IsMelee: false,
                IsSpell: true, DistanceInches: 15f);

            Assert.That(spellHits.ShootingDistanceInches, Is.Null);
            Assert.That(spellSaves.ShootingDistanceInches, Is.Null);
            Assert.That(Holds(RangeConditional36Definitions.Attacking, spellHits, _attacker), Is.False);
            Assert.That(Holds(RangeConditional36Definitions.Attacked, spellSaves, _target), Is.False);
        }

        [Test]
        public void TheSaveContext_ReadsAChargeFromItsRecordedOrigin()
        {
            // The save stage carries no IsCharging; only the charger's swings carry a non-zero origin.
            var longCharge = new SaveRollCompleteContext(_attacker, _target, TestDice.Faces(1), IsMelee: true,
                DistanceInches: 0.5f, ChargeOriginDistanceInches: 7f);
            var strikeBack = new SaveRollCompleteContext(_attacker, _target, TestDice.Faces(1), IsMelee: true,
                DistanceInches: 0.5f, ChargeOriginDistanceInches: 0f);
            var shot = new SaveRollCompleteContext(_attacker, _target, TestDice.Faces(1), IsMelee: false,
                DistanceInches: 10f);

            Assert.That(longCharge.ChargeDistanceInches, Is.EqualTo(7f));
            Assert.That(strikeBack.ChargeDistanceInches, Is.Null);
            Assert.That(shot.ShootingDistanceInches, Is.EqualTo(10f));
            Assert.That(Holds(RangeConditional36Definitions.Attacking, longCharge, _attacker), Is.True);
            Assert.That(Holds(RangeConditional36Definitions.Attacking, strikeBack, _attacker), Is.False);
        }

        [Test]
        public void TheMoraleContext_ReadsTheAttackThatTriggeredTheTest()
        {
            Assert.That(Holds(RangeConditional36Definitions.Attacked, new PreMoraleTestContext(_target), _target),
                Is.False, "a test nothing like that triggered (a spell, Mind Control) carries no distance");
            Assert.That(Holds(RangeConditional36Definitions.Attacked,
                new PreMoraleTestContext(_target, ShotFromInches: 10f), _target), Is.True);
            Assert.That(Holds(RangeConditional36Definitions.Attacked,
                new PreMoraleTestContext(_target, ShotFromInches: 8f), _target), Is.False);
            Assert.That(Holds(RangeConditional36Definitions.Attacked,
                new PreMoraleTestContext(_target, ChargedFromInches: 7f), _target), Is.True);
            Assert.That(Holds(RangeConditional36Definitions.Attacked,
                new PreMoraleTestContext(_target, ChargedFromInches: 5f), _target), Is.False);
        }

        [Test]
        public void TheNewKinds_RoundTrip()
        {
            Condition attacking = RangeConditional36Definitions.Attacking;
            string json = JsonSerializer.Serialize(attacking, RuleJson.Options);
            Assert.That(json, Does.Contain("\"kind\": \"attackingFromOver\""));
            Assert.That(json, Does.Contain("\"shootInches\": 9").And.Contain("\"chargeInches\": 6"));
            Assert.That(JsonSerializer.Deserialize<Condition>(json, RuleJson.Options), Is.EqualTo(attacking));

            Condition attacked = new Condition.And(RangeConditional36Definitions.Attacked,
                new Condition.MostModelsHaveThisRule());
            string attackedJson = JsonSerializer.Serialize(attacked, RuleJson.Options);
            Assert.That(attackedJson, Does.Contain("\"kind\": \"attackedFromOver\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(attackedJson, RuleJson.Options), Is.EqualTo(attacked));
        }

        [Test]
        public void TheRangeModifier_CarriesAChargeHalf_AndOldDataStillParses()
        {
            Effect both = new Effect.RangeModifier(Delta: -4, MinResultInches: 6, ChargeDelta: -2);
            string json = JsonSerializer.Serialize(both, RuleJson.Options);
            Assert.That(json, Does.Contain("\"chargeDelta\": -2"));
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(both));

            // A pre-#432 file has no chargeDelta: shooting-only, exactly as before.
            const string legacy = "{ \"kind\": \"rangeModifier\", \"delta\": -6, \"minResultInches\": 6 }";
            Assert.That(JsonSerializer.Deserialize<Effect>(legacy, RuleJson.Options),
                Is.EqualTo(new Effect.RangeModifier(-6, 6)));

            var produced = new System.Collections.Generic.List<RuleOperation>();
            both.Apply(new RuleInvocation(null, _target, System.Array.Empty<RuleArgument>()), produced);
            Assert.That(produced.Single(), Is.EqualTo(new RuleOperation.ApplyRangeModifier(-4, 6, -2)));
        }

        [Test]
        public void TheRangeOp_SaysBothHalves()
        {
            Assert.That(new RuleOperation.ApplyRangeModifier(-4, 6, -2).Describe(),
                Is.EqualTo("changed shooting range by -4\" and charge range by -2\" (no lower than 6\")"));
            Assert.That(new RuleOperation.ApplyRangeModifier(0, 6, -2).Describe(),
                Is.EqualTo("changed charge range by -2\" (no lower than 6\")"));
            Assert.That(new RuleOperation.ApplyRangeModifier(4, 0, 2).Describe(),
                Is.EqualTo("changed shooting range by +4\" and charge range by +2\""));
            Assert.That(new RuleOperation.ApplyRangeModifier(6).Describe(), Is.EqualTo("changed shooting range by +6\""),
                "a shooting-only op reads exactly as before");
        }

        [TestCase(EHookID.Shooting_OnHitRollModifier)]
        [TestCase(EHookID.Shooting_OnHitRollComplete)]
        [TestCase(EHookID.Shooting_OnSaveRollComplete)]
        [TestCase(EHookID.Morale_OnPreMoraleTest)]
        public void TheValidator_AcceptsTheConditions_WhereTheDistanceIsKnown(EHookID hook)
        {
            var rule = new SpecialRuleDefinition("Probe",
                new[]
                {
                    new HookEntry(hook, new Condition.And(RangeConditional36Definitions.Attacked,
                            new Condition.MostModelsHaveThisRule()),
                        new Effect.RollModifier(ERollKind.Morale, 1), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>());

            Assert.That(new RuleValidator().Validate(rule), Is.Empty);
        }

        [Test]
        public void TheValidator_RefusesTheConditions_WhereNoAttackIsInFlight()
        {
            var rule = new SpecialRuleDefinition("Probe",
                new[]
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared, RangeConditional36Definitions.Attacking,
                        new Effect.MovementBonus(EActionType.Advance, 1f), ELifetime.ThisActivation),
                },
                System.Array.Empty<ActivatedAbility>());

            Assert.That(new RuleValidator().Validate(rule).Select(v => v.MissingCapability),
                Has.Member(typeof(IHasShootChargeDistance)));
        }
    }
}
