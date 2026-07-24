using System;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 misc - Quick Readjustment ("this model ignores penalties from shooting after moving when using
    // Indirect weapons"). Indirect's penalty is a weapon-scoped -1 at Shooting_OnHitRollModifier gated on
    // And(AfterMoving, Not(IsMelee)). Quick Readjustment is authored weapon-scoped and routed onto every
    // weapon (slice 0); a new Condition.WeaponHasRule lets it fire its +1 ONLY on the weapon that also
    // carries Indirect, cancelling that rule's -1. The net a player sees is what's asserted here.
    [TestFixture]
    public class QuickReadjustmentRuleIntegrationTests
    {
        private static SpecialRuleDefinition QuickReadjustment() =>
            new("Quick Readjustment",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.And(new Condition.WeaponHasRule("Indirect"),
                            new Condition.And(new Condition.AfterMoving(),
                                new Condition.Not(new Condition.IsMelee()))),
                        new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>(),
                ERuleScope.Weapon);

        private static int NetHitAfterMoving(TestRuleHarness harness, IUnit attacker, IUnit defender,
            IWeapon weapon)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, DistanceInches: 12f, AttackerMoved: true,
                    IsMelee: false),
                RuleParticipant.Actor(attacker, weapon)));
            return sink.Net(ERollKind.Hit);
        }

        [Test]
        public void IndirectWeapon_AfterMoving_HasTheMinusOnePenalty()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);
            var mortar = new Weapon("Mortar", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            mortar.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));

            Assert.That(NetHitAfterMoving(harness, attacker, defender, mortar), Is.EqualTo(-1),
                "baseline: Indirect penalises shooting after moving.");
        }

        [Test]
        public void QuickReadjustment_CancelsThePenalty_OnAnIndirectWeapon()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);
            var mortar = new Weapon("Mortar", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            mortar.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            mortar.AttachRuleDefinition(new ResolvedRule("Quick Readjustment", QuickReadjustment()));

            Assert.That(NetHitAfterMoving(harness, attacker, defender, mortar), Is.EqualTo(0),
                "Indirect -1 + Quick Readjustment +1 = 0: the after-moving penalty is ignored.");
        }

        [Test]
        public void QuickReadjustment_DoesNothing_OnANonIndirectWeapon()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);
            // A plain rifle (no Indirect, so no penalty to cancel) that was routed Quick Readjustment anyway.
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            rifle.AttachRuleDefinition(new ResolvedRule("Quick Readjustment", QuickReadjustment()));

            Assert.That(NetHitAfterMoving(harness, attacker, defender, rifle), Is.EqualTo(0),
                "WeaponHasRule(Indirect) is false here, so the +1 never fires - Quick Readjustment must not " +
                "become a blanket after-moving hit bonus on ordinary weapons.");
        }

        [Test]
        public void QuickReadjustment_GivesNoBonus_WhenTheUnitDidNotMove()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);
            var mortar = new Weapon("Mortar", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            mortar.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            mortar.AttachRuleDefinition(new ResolvedRule("Quick Readjustment", QuickReadjustment()));

            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, DistanceInches: 12f, AttackerMoved: false,
                    IsMelee: false),
                RuleParticipant.Actor(attacker, mortar)));

            Assert.That(sink.Net(ERollKind.Hit), Is.EqualTo(0),
                "no move -> no Indirect penalty and no Quick Readjustment bonus; +1 with nothing to cancel " +
                "would wrongly buff a stationary Indirect shot.");
        }
    }
}
