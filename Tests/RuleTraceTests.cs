using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #163 — the rule-trace channel. With RuleTrace.Enabled, live evaluations narrate through
    // ITextOutput.LogDebug: hook headers, per-entry condition outcomes, suppression victims, and
    // ability-offer decisions. Read-only query paths and disabled tracing must stay silent — the
    // per-frame UI queries share the evaluator, so a leak here is a per-frame log flood.
    [TestFixture]
    public class RuleTraceTests
    {
        private sealed class CapturingOutput : ITextOutput
        {
            public readonly List<string> Lines = new();
            public readonly List<string> DebugLines = new();
            public void Log(string message, TextColor? color = null) => Lines.Add(message);
            public void LogDebug(string message, TextColor? color = null) => DebugLines.Add(message);
        }

        private TestRuleHarness _harness = null!;
        private CapturingOutput _output = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _harness = new TestRuleHarness();
            _output = new CapturingOutput();
            _evaluator = new RuleEvaluator(_harness.GameContext.DiceRoller, _output, _harness.Resolver);
            RuleTrace.Enabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            // Process-wide switch — never leak an enabled trace into other fixtures.
            RuleTrace.Enabled = false;
        }

        [Test]
        public void LiveEvaluation_TracesHookHeaderAndFiredEntry()
        {
            _harness.Register(CoreRuleCatalog.Furious);
            IUnit attacker = _harness.BuildUnit("A", 3, "Furious");
            IUnit defender = _harness.BuildUnit("B", 3);

            var context = new HitRollCompleteContext(attacker, defender,
                TestDice.Faces(6, 3), IsMelee: true, IsCharging: true);
            _evaluator.EvaluateAll(context, (attacker, ERuleSeat.Actor), (defender, ERuleSeat.Subject));

            Assert.That(_output.DebugLines, Has.Some.Contains("Shooting_OnHitRollComplete fires"));
            Assert.That(_output.DebugLines, Has.Some.Match(".*Furious.*fired -> InsertExtraHits.*"));
        }

        [Test]
        public void LiveEvaluation_TracesFailedCondition_WithItsDescription()
        {
            _harness.Register(CoreRuleCatalog.Stealth);
            IUnit attacker = _harness.BuildUnit("A", 3);
            IUnit defender = _harness.BuildUnit("B", 3, "Stealth");

            // 5" — inside Stealth's >9" gate, so the condition fails and the trace must say which.
            var context = new HitRollModifierContext(attacker, defender, DistanceInches: 5f);
            _evaluator.EvaluateAll(context, (attacker, ERuleSeat.Actor), (defender, ERuleSeat.Subject));

            Assert.That(_output.DebugLines,
                Has.Some.Match(".*Stealth.*condition And\\(DistanceGreaterThan, AllModelsHaveThisRule\\) not met.*"));
        }

        [Test]
        public void LiveEvaluation_TracesSuppressedVictim_NamingTheSuppressor()
        {
            _harness.Register(CoreRuleCatalog.Regeneration);
            _harness.Register(CoreRuleCatalog.Bane);
            IUnit attacker = _harness.BuildUnit("A", 3, "Bane");
            IUnit defender = _harness.BuildUnit("B", 3, "Regeneration");

            var context = new SaveRollCompleteContext(attacker, defender, TestDice.Faces(6));
            _evaluator.EvaluateAll(context, (attacker, ERuleSeat.Actor), (defender, ERuleSeat.Subject));

            Assert.That(_output.DebugLines, Has.Some.Match(".*Regeneration IgnoreWound suppressed by Bane.*"));
        }

        [Test]
        public void ReadOnlyNamedQuery_NeverTraces_EvenWhenEnabled()
        {
            _harness.Register(CoreRuleCatalog.Furious);
            IUnit attacker = _harness.BuildUnit("A", 3, "Furious");
            IUnit defender = _harness.BuildUnit("B", 3);

            var context = new HitRollCompleteContext(attacker, defender,
                TestDice.Faces(6), IsMelee: true, IsCharging: true);
            _evaluator.EvaluateAllNamed(context, (attacker, ERuleSeat.Actor), (defender, ERuleSeat.Subject));

            Assert.That(_output.DebugLines, Is.Empty,
                "log:false query paths run per-frame while building UI and must not trace.");
        }

        [Test]
        public void DisabledTrace_EmitsNothing_AndNormalLogIsUnchanged()
        {
            RuleTrace.Enabled = false;
            _harness.Register(CoreRuleCatalog.Furious);
            IUnit attacker = _harness.BuildUnit("A", 3, "Furious");
            IUnit defender = _harness.BuildUnit("B", 3);

            var context = new HitRollCompleteContext(attacker, defender,
                TestDice.Faces(6), IsMelee: true, IsCharging: true);
            _evaluator.EvaluateAll(context, (attacker, ERuleSeat.Actor), (defender, ERuleSeat.Subject));

            Assert.That(_output.DebugLines, Is.Empty);
            Assert.That(_output.Lines, Has.Some.Contains("Furious"),
                "The player-facing log line for a fired rule must not depend on tracing.");
        }

        [Test]
        public void GatherOffers_TracesOfferedAndUnaffordable()
        {
            _harness.Register(CoreRuleCatalog.Strafing);
            IUnit mover = _harness.BuildUnit("A", 3, "Strafing");

            // Strafing's ability is once-per-activation: first gather is offered, and after the used
            // marker is applied the second gather traces the cost refusal.
            var context = new MoveThroughEnemyContext(mover);
            IReadOnlyList<AbilityOffer> offers = _evaluator.GatherOffers(context);
            Assert.That(offers, Has.Count.EqualTo(1));
            Assert.That(_output.DebugLines, Has.Some.Match(".*Strafing ability at Movement_OnMoveThroughEnemy: offered.*"));

            OperationApplier.ApplyTokenOperations(_evaluator.ResolveAbility(offers[0], new[] { mover }));
            _output.DebugLines.Clear();

            _evaluator.GatherOffers(context);
            Assert.That(_output.DebugLines, Has.Some.Match(".*Strafing.*not offered \\(cannot pay.*"));
        }
    }
}
