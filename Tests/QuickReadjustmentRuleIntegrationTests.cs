using System;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 misc - Quick Readjustment (3.5: "this model ignores penalties from shooting after moving when using
    // Indirect weapons"), as it stands between #425 and its deletion by #436 (3.6 drops the rule).
    //
    // 3.5 authored it weapon-scoped with a +1 at Shooting_OnHitRollModifier, gated on WeaponHasRule("Indirect") and
    // AfterMoving, to cancel Indirect's -1. Core Rules v3.6.0 Indirect has no after-moving penalty (#425), so that
    // +1 would no longer cancel anything - it would be a free buff. The shipped supplement entry is therefore inert
    // until #436 removes it (QuickReadjustmentShippedDataTests pins that app-side); the net a player sees is
    // asserted here. Legacy note (#425 D5): the bundled 3.5 .fdgbook files (and any saved .fdgarmy) embed the old
    // +1 shape until #438 re-bakes them from 3.6 books, none of which names Quick Readjustment.
    [TestFixture]
    public class QuickReadjustmentRuleIntegrationTests
    {
        // The 3.5 shape, as embedded in old books and army files.
        private static SpecialRuleDefinition QuickReadjustment35() =>
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

        // The interim shipped shape (#425): a weapon-scoped marker with nothing to do.
        private static SpecialRuleDefinition QuickReadjustmentInert() =>
            new("Quick Readjustment", Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), ERuleScope.Weapon);

        private static int NetHit(TestRuleHarness harness, IUnit attacker, IUnit defender, IWeapon weapon, bool moved)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, DistanceInches: 12f, AttackerMoved: moved,
                    IsMelee: false),
                RuleParticipant.Actor(attacker, weapon)));
            return sink.Net(ERollKind.Hit);
        }

        private static Weapon Mortar()
        {
            var mortar = new Weapon("Mortar", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            mortar.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            return mortar;
        }

        [Test]
        public void IndirectWeapon_AfterMoving_HasNoPenaltyToCancel()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);

            Assert.That(NetHit(harness, attacker, defender, Mortar(), moved: true), Is.EqualTo(0),
                "3.6 Indirect: no -1 to hit after moving (#425).");
        }

        [Test]
        public void QuickReadjustment_AsShipped_IsInert()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);
            Weapon mortar = Mortar();
            mortar.AttachRuleDefinition(new ResolvedRule("Quick Readjustment", QuickReadjustmentInert()));

            Assert.That(NetHit(harness, attacker, defender, mortar, moved: true), Is.EqualTo(0),
                "nothing to ignore, so nothing changes after moving...");
            Assert.That(NetHit(harness, attacker, defender, mortar, moved: false), Is.EqualTo(0), "...or on a Hold.");
        }

        // Why the shipped entry had to change: the 3.5 +1, left as it was, now nets a +1 the rules never grant.
        [Test]
        public void QuickReadjustment_The35Shape_WouldNowBeAFreeBuff()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);
            Weapon mortar = Mortar();
            mortar.AttachRuleDefinition(new ResolvedRule("Quick Readjustment", QuickReadjustment35()));

            Assert.That(NetHit(harness, attacker, defender, mortar, moved: true), Is.EqualTo(1),
                "with Indirect's -1 gone, the old +1 has nothing to cancel.");
        }

        [Test]
        public void QuickReadjustment_The35Shape_StillDoesNothingOnANonIndirectWeapon()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);
            // A plain rifle (no Indirect) that was routed Quick Readjustment anyway.
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            rifle.AttachRuleDefinition(new ResolvedRule("Quick Readjustment", QuickReadjustment35()));

            Assert.That(NetHit(harness, attacker, defender, rifle, moved: true), Is.EqualTo(0),
                "WeaponHasRule(Indirect) is false here, so the +1 never fires.");
        }
    }
}
