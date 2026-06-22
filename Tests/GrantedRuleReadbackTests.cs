using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #100 Part 1, slice 1 — granted-rule read-back. <c>Effect.Aura</c>/<c>Effect.AddRule</c> already
    /// write a <see cref="TokenType.RuleGrant"/> token; these pin that the evaluator now READS it back,
    /// so a unit holding the token behaves as if it had the named rule. Regeneration is the probe rule:
    /// it fires unconditionally at save-roll-complete on the Subject seat, producing an
    /// <see cref="RuleOperation.IgnoreWound"/> that's trivial to assert on.
    /// </summary>
    [TestFixture]
    public class GrantedRuleReadbackTests
    {
        private static SaveRollCompleteContext SaveContext(IUnit attacker, IUnit defender) =>
            new(attacker, defender, TestDice.Faces(4, 4, 4));

        // A unit that doesn't statically have Regeneration, but holds a RuleGrant token for it,
        // regenerates — the granted rule fires exactly as a static attachment would.
        [Test]
        public void GrantedRule_FiresAsIfStaticallyAttached()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);

            IUnit defender = harness.BuildUnit("P1", modelCount: 3);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);
            harness.GrantRule(defender, "Regeneration");

            var ops = harness.Evaluate(defender, ERuleSeat.Subject, SaveContext(attacker, defender));

            ops.HasOperation<RuleOperation.IgnoreWound>(op => op.MinRoll == 5);
        }

        // Control: the same unit WITHOUT the grant produces no Regeneration effect — the read-back
        // is what makes the difference, not something else in the path.
        [Test]
        public void WithoutGrant_RuleDoesNotFire()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);

            IUnit defender = harness.BuildUnit("P1", modelCount: 3);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);

            var ops = harness.Evaluate(defender, ERuleSeat.Subject, SaveContext(attacker, defender));

            Assert.That(ops.OfType<RuleOperation.IgnoreWound>(), Is.Empty);
        }

        // No resolver (the resume path before #095 rehydration, or a bare-evaluator construction) makes
        // granted-rule read-back a safe no-op: the token contributes nothing rather than throwing.
        [Test]
        public void NullResolver_GrantedRuleIsSilentlyIgnored()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);

            IUnit defender = harness.BuildUnit("P1", modelCount: 3);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);
            harness.GrantRule(defender, "Regeneration");

            var resolverless = new RuleEvaluator(harness.GameContext.DiceRoller);
            var ops = resolverless.Evaluate(defender, ERuleSeat.Subject, SaveContext(attacker, defender));

            Assert.That(ops.OfType<RuleOperation.IgnoreWound>(), Is.Empty);
        }

        // An unregistered grant name is skipped, not thrown — a granted rule the registry doesn't carry
        // just does nothing (matches army-load's skip-and-warn for unimplemented rule names).
        [Test]
        public void UnknownGrantName_IsSkippedNotThrown()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);

            IUnit defender = harness.BuildUnit("P1", modelCount: 3);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);
            harness.GrantRule(defender, "No Such Rule");

            var ops = harness.Evaluate(defender, ERuleSeat.Subject, SaveContext(attacker, defender));

            Assert.That(ops.OfType<RuleOperation.IgnoreWound>(), Is.Empty);
        }

        // The rulebook's "multiple instances of the same special rule don't stack": a unit that has
        // Regeneration both statically and by grant regenerates ONCE (argument-less dedup keys on the
        // shared definition, so the granted copy collapses into the static one).
        [Test]
        public void GrantedAndStatic_SameArgumentlessRule_FiresOnce()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);

            IUnit defender = harness.BuildUnit("P1", modelCount: 3, "Regeneration");
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);
            harness.GrantRule(defender, "Regeneration");

            var ops = harness.Evaluate(defender, ERuleSeat.Subject, SaveContext(attacker, defender));

            Assert.That(ops.OfType<RuleOperation.IgnoreWound>().Count(), Is.EqualTo(1));
        }

        // End-to-end aura loop: an aura rule emits the grant op, and once that op lands on the unit's
        // tokens the granted rule actually behaves — closing Aura → grant → read-back → effect.
        [Test]
        public void Aura_GrantThenReadBack_GrantedRuleBehaves()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);
            harness.Register(new SpecialRuleDefinition("Regeneration Aura",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                        new Condition.Always(),
                        new Effect.Aura("Regeneration"),
                        ELifetime.Aura),
                },
                System.Array.Empty<ActivatedAbility>()));

            IUnit unit = harness.BuildUnit("P1", modelCount: 4, "Regeneration Aura");
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);

            // 1) The aura produces a grant op for its whole unit...
            var grantOps = harness.Evaluate(unit, ERuleSeat.Actor, new UnitCreatedContext(unit));
            var grant = grantOps.OfType<RuleOperation.GrantTokenToUnit>().Single(op =>
                op.TokenToGrant.Payload is TokenPayload.RuleGrant rg && rg.RuleName == "Regeneration");

            // 2) ...which a stage applies to the unit's container...
            grant.Unit.Tokens.AddToken(grant.TokenToGrant);

            // 3) ...and now the granted Regeneration fires when the unit is wounded.
            var ops = harness.Evaluate(unit, ERuleSeat.Subject, SaveContext(attacker, unit));

            ops.HasOperation<RuleOperation.IgnoreWound>(op => op.MinRoll == 5);
        }

        // Two distinct grants on one unit are stored as separate tokens and BOTH fire — the payload-aware
        // merge fix (#2c). Before it, the second grant's token collapsed into the first's count, dropping it.
        [Test]
        public void TwoDistinctGrants_AreStoredSeparately_AndBothFire()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);
            harness.Register(new SpecialRuleDefinition("Tough Skin",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Save, 1), ELifetime.ThisAttack, ERuleSeat.Subject),
                },
                System.Array.Empty<ActivatedAbility>()));

            IUnit defender = harness.BuildUnit("P1", modelCount: 3);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);
            harness.GrantRule(defender, "Regeneration");
            harness.GrantRule(defender, "Tough Skin");

            var ops = harness.Evaluate(defender, ERuleSeat.Subject, SaveContext(attacker, defender));

            ops.HasOperation<RuleOperation.IgnoreWound>(op => op.MinRoll == 5);
            ops.HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Save && op.Delta == 1);
        }

        // A "next time it would apply" grant (FirstTrigger clear) fires once: the live EvaluateAll consumes
        // it DIRECTLY (#101), so it no longer fires — without the caller applying any returned op, which is
        // what makes it work at the combat hooks whose ops run through sinks rather than OperationApplier.
        [Test]
        public void FirstTriggerGrant_FiresOnce_ThenIsConsumed()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);

            IUnit defender = harness.BuildUnit("P1", modelCount: 3);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);
            harness.GrantRule(defender, "Regeneration", ELifetime.NextTrigger, clear: new TokenClearTrigger.FirstTrigger());

            // EvaluateAll is the live apply path; it spends the one-shot grant on this occurrence.
            var ops = harness.EvaluateAll(SaveContext(attacker, defender), (defender, ERuleSeat.Subject));
            ops.HasOperation<RuleOperation.IgnoreWound>(op => op.MinRoll == 5);

            var after = harness.EvaluateAll(SaveContext(attacker, defender), (defender, ERuleSeat.Subject));
            Assert.That(after.OfType<RuleOperation.IgnoreWound>(), Is.Empty, "spent grant no longer fires");
        }

        // An aura grant (ManualOnly clear) is NOT consumed — it persists and fires every time.
        [Test]
        public void AuraGrant_IsNotConsumedOnFire()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Regeneration);

            IUnit defender = harness.BuildUnit("P1", modelCount: 3);
            IUnit attacker = harness.BuildUnit("P2", modelCount: 3);
            harness.GrantRule(defender, "Regeneration"); // default: Aura / ManualOnly

            harness.EvaluateAll(SaveContext(attacker, defender), (defender, ERuleSeat.Subject));
            var after = harness.EvaluateAll(SaveContext(attacker, defender), (defender, ERuleSeat.Subject));

            after.HasOperation<RuleOperation.IgnoreWound>(op => op.MinRoll == 5);
        }
    }
}
