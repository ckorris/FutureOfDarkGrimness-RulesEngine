using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #100 Part 1, slice 2 — the five <see cref="Condition"/> subtypes whose <c>Evaluate</c> bodies
    /// were stubbed (throwing). Each is gated onto a probe rule that produces a roll modifier when the
    /// condition passes, so "did the gate open?" reads as "is the op present?". The target-keyed three
    /// ride the new <see cref="IHasTarget"/> capability, exercised here through HitRollCompleteContext.
    /// </summary>
    [TestFixture]
    public class ConditionEvaluationTests
    {
        private static SpecialRuleDefinition ProbeAtHitComplete(string name, Condition gate,
            ERollKind roll = ERollKind.Save, int delta = -2) =>
            new(name,
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete, gate,
                        new Effect.RollModifier(roll, delta), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>());

        private static HitRollCompleteContext HitContext(IUnit attacker, IUnit target) =>
            new(attacker, target, TestDice.Faces(4));

        private static bool Fired(System.Collections.Generic.IReadOnlyList<RuleOperation> ops) =>
            ops.OfType<RuleOperation.ApplyRollModifier>().Any();

        // ── UnitHasRule (no target needed) ──────────────────────────────────────────

        [Test]
        public void UnitHasRule_WhenBearerHasNamedRule_Passes()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Stealth);
            harness.Register(ProbeAtHitComplete("Boost", new Condition.UnitHasRule("Stealth")));

            IUnit attacker = harness.BuildUnit("P1", 1, "Boost", "Stealth");
            IUnit target = harness.BuildUnit("P2", 1);

            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, target))), Is.True);
        }

        [Test]
        public void UnitHasRule_WhenBearerLacksNamedRule_DoesNotPass()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Stealth);
            harness.Register(ProbeAtHitComplete("Boost", new Condition.UnitHasRule("Stealth")));

            IUnit attacker = harness.BuildUnit("P1", 1, "Boost");
            IUnit target = harness.BuildUnit("P2", 1);

            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, target))), Is.False);
        }

        // ── Or ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Or_PassesWhenEitherSidePasses()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Stealth);
            harness.Register(CoreRuleCatalog.Scout);
            harness.Register(ProbeAtHitComplete("OrGate",
                new Condition.Or(new Condition.UnitHasRule("Stealth"), new Condition.UnitHasRule("Scout"))));

            IUnit attacker = harness.BuildUnit("P1", 1, "OrGate", "Scout"); // only the right side holds
            IUnit target = harness.BuildUnit("P2", 1);

            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, target))), Is.True);
        }

        [Test]
        public void Or_FailsWhenNeitherSidePasses()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Stealth);
            harness.Register(CoreRuleCatalog.Scout);
            harness.Register(ProbeAtHitComplete("OrGate",
                new Condition.Or(new Condition.UnitHasRule("Stealth"), new Condition.UnitHasRule("Scout"))));

            IUnit attacker = harness.BuildUnit("P1", 1, "OrGate");
            IUnit target = harness.BuildUnit("P2", 1);

            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, target))), Is.False);
        }

        // ── TargetHasRule ─────────────────────────────────────────────────────────────

        [Test]
        public void TargetHasRule_KeysOffTheTargetNotTheBearer()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Stealth);
            harness.Register(ProbeAtHitComplete("VsStealth", new Condition.TargetHasRule("Stealth")));

            IUnit attacker = harness.BuildUnit("P1", 1, "VsStealth");
            IUnit stealthyTarget = harness.BuildUnit("P2", 1, "Stealth");
            IUnit plainTarget = harness.BuildUnit("P2", 1);

            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, stealthyTarget))), Is.True);
            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, plainTarget))), Is.False);
        }

        // ── TargetMajorityHasTough ────────────────────────────────────────────────────

        [Test]
        public void TargetMajorityHasTough_PassesOnlyWhenMostModelsAreToughEnough()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProbeAtHitComplete("Slayer", new Condition.TargetMajorityHasTough(3)));

            IUnit attacker = harness.BuildUnit("P1", 1, "Slayer");

            IUnit toughUnit = harness.BuildUnit("P2", 3);
            foreach (IModel m in toughUnit.Models) m.SetMaxWounds(3);

            IUnit mixedUnit = harness.BuildUnit("P2", 3);
            mixedUnit.Models[0].SetMaxWounds(3); // 1 of 3 → not a majority

            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, toughUnit))), Is.True);
            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, mixedUnit))), Is.False);
        }

        // ── StatGreaterOrEqualTo ──────────────────────────────────────────────────────

        [Test]
        public void StatGreaterOrEqualTo_ReadsTheTargetUnitStat()
        {
            var harness = new TestRuleHarness();
            // BuildUnit fixes Defense at 4.
            harness.Register(ProbeAtHitComplete("VsDef4",
                new Condition.StatGreaterOrEqualTo(EStatKind.Defense, 4)));
            harness.Register(ProbeAtHitComplete("VsDef5",
                new Condition.StatGreaterOrEqualTo(EStatKind.Defense, 5)));

            IUnit target = harness.BuildUnit("P2", 1);

            IUnit a4 = harness.BuildUnit("P1", 1, "VsDef4");
            IUnit a5 = harness.BuildUnit("P1", 1, "VsDef5");

            Assert.That(Fired(harness.Evaluate(a4, ERuleSeat.Actor, HitContext(a4, target))), Is.True);
            Assert.That(Fired(harness.Evaluate(a5, ERuleSeat.Actor, HitContext(a5, target))), Is.False);
        }

        [Test]
        public void StatGreaterOrEqualTo_Tough_UsesMajoritySemantics()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProbeAtHitComplete("VsTough3",
                new Condition.StatGreaterOrEqualTo(EStatKind.Tough, 3)));

            IUnit attacker = harness.BuildUnit("P1", 1, "VsTough3");
            IUnit toughUnit = harness.BuildUnit("P2", 2);
            foreach (IModel m in toughUnit.Models) m.SetMaxWounds(3);

            Assert.That(Fired(harness.Evaluate(attacker, ERuleSeat.Actor, HitContext(attacker, toughUnit))), Is.True);
        }
    }
}
