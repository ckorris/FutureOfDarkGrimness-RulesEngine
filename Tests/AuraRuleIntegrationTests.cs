using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #101 — Aura projection through the real creation pass. <c>Effect.Aura</c> fires at
    /// <see cref="EHookID.Lifecycle_OnUnitCreated"/> and emits a <see cref="RuleOperation.GrantTokenToUnit"/>;
    /// <see cref="UnitCreationRules.Apply"/> (the exact call <c>FDGServer</c> makes at army-load) must now
    /// APPLY that grant so the named rule projects unit-wide. The read-back half (<c>CollectGrantedRules</c>)
    /// is covered by <see cref="GrantedRuleReadbackTests"/>; that suite seeds the token by hand. This suite's
    /// distinction is that nothing adds the token manually — the live creation applicator does, end to end.
    /// Regeneration is the probe (fires unconditionally at save-roll-complete on the Subject seat → an
    /// <see cref="RuleOperation.IgnoreWound"/> that is trivial to assert on). GDF auras are unit-scoped (every
    /// model in the bearer's own unit), never distance-based — the grant lands on the unit, not a radius.
    /// </summary>
    [TestFixture]
    public class AuraRuleIntegrationTests
    {
        private static SpecialRuleDefinition RegenerationAura() => new("Regeneration Aura",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnUnitCreated, new Condition.Always(),
                    new Effect.Aura("Regeneration"), ELifetime.Aura),
            },
            Array.Empty<ActivatedAbility>());

        private static SaveRollCompleteContext SaveContext(IUnit attacker, IUnit defender) =>
            new(attacker, defender, TestDice.Faces(4, 4, 4));

        // The headline: a unit carrying an aura rule, run through the live creation applicator, ends up
        // holding the granted rule's token AND behaves as if it had that rule — without anything seeding
        // the token by hand. This is the wire that was missing (the grant op was produced then dropped).
        [Test]
        public void AuraRule_ThroughCreationPass_GrantsAndProjectsUnitWide()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);
            harness.Register(RegenerationAura());

            IUnit defender = harness.BuildUnit("P1", modelCount: 4, "Regeneration Aura");
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);

            UnitCreationRules.Apply(defender, harness.Evaluator);

            Assert.That(defender.Tokens.GetTokenCount(TokenType.RuleGrant), Is.EqualTo(1),
                "the aura's GrantTokenToUnit must actually land on the unit at creation.");

            var ops = harness.Evaluate(defender, ERuleSeat.Subject, SaveContext(attacker, defender));
            ops.HasOperation<RuleOperation.IgnoreWound>(op => op.MinRoll == 5);
        }

        // Control: the same unit type WITHOUT the aura rule gets no grant and no Regeneration effect, so the
        // creation pass is what makes the difference rather than something incidental in the path.
        [Test]
        public void NoAuraRule_ThroughCreationPass_GrantsNothing()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);

            IUnit defender = harness.BuildUnit("P1", modelCount: 4);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);

            UnitCreationRules.Apply(defender, harness.Evaluator);

            Assert.That(defender.Tokens.GetTokenCount(TokenType.RuleGrant), Is.EqualTo(0));

            var ops = harness.Evaluate(defender, ERuleSeat.Subject, SaveContext(attacker, defender));
            Assert.That(ops.OfType<RuleOperation.IgnoreWound>(), Is.Empty);
        }

        // The aura is granted once per creation — re-running the pass would double-grant, so it must not be
        // invoked twice on the same unit in production (it isn't: FDGServer calls it once per unit). This pins
        // the single-grant expectation so a future caller change that double-fires creation is caught.
        [Test]
        public void AuraRule_SingleCreationPass_GrantsExactlyOnce()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);
            harness.Register(RegenerationAura());

            IUnit defender = harness.BuildUnit("P1", modelCount: 4, "Regeneration Aura");

            UnitCreationRules.Apply(defender, harness.Evaluator);

            Assert.That(defender.Tokens.GetTokenCount(TokenType.RuleGrant), Is.EqualTo(1));
        }

        // #382: the aura arrives on a JOINED HERO. The join relocates the hero's unit-scoped rules onto the
        // hero MODEL and consumes its standalone unit before any creation pass runs, so the host's creation
        // pass is the only place the aura can fire — and its participant walks no models, which left every
        // hero-carried aura inert (the reported Robot Legions shape: a lord's Reanimation Aura dead on
        // exactly the unit it was bought for). The grant must land on the HOST and project unit-wide.
        [Test]
        public void JoinedHeroAura_ThroughCreationPass_GrantsAndProjectsOverHost()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);
            harness.Register(RegenerationAura());

            var host = (UnitData)harness.BuildUnit("P1", modelCount: 4);
            var hero = (UnitData)harness.BuildUnit("P1", modelCount: 1, "Regeneration Aura");
            harness.AttachRule(hero, CoreRuleCatalog.Hero);
            Join(host, hero);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);

            UnitCreationRules.Apply(host, harness.Evaluator);

            Assert.That(host.Tokens.GetTokenCount(TokenType.RuleGrant), Is.EqualTo(1),
                "the hero-carried aura must land its grant on the host at the host's creation pass.");
            var ops = harness.Evaluate(host, ERuleSeat.Subject, SaveContext(attacker, host));
            ops.HasOperation<RuleOperation.IgnoreWound>(op => op.MinRoll == 5);
        }

        // #382 dedup: the host statically carries the same aura the hero brings. The static copy fired in
        // the main creation pass; the hero pass must not stack a second grant on top (the rulebook's
        // "multiple instances of the same rule don't stack", the evaluator's argument-less dedup).
        [Test]
        public void JoinedHeroAura_HostCarriesSameAura_GrantsExactlyOnce()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);
            harness.Register(RegenerationAura());

            var host = (UnitData)harness.BuildUnit("P1", modelCount: 4, "Regeneration Aura");
            var hero = (UnitData)harness.BuildUnit("P1", modelCount: 1, "Regeneration Aura");
            harness.AttachRule(hero, CoreRuleCatalog.Hero);
            Join(host, hero);

            UnitCreationRules.Apply(host, harness.Evaluator);

            Assert.That(host.Tokens.GetTokenCount(TokenType.RuleGrant), Is.EqualTo(1));
        }

        // #006's real merge — the same call army-load makes, so the hero's rules genuinely relocate onto
        // the hero model rather than being hand-placed there.
        private static void Join(UnitData host, UnitData hero)
        {
            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });
        }
    }
}
