using System;
using System.Collections.Generic;
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
    // RuleOperation.Describe() is what the game log prints for every rule that fires:
    // "{bearer}'s {rule} {Describe()}." It used to be a VIRTUAL returning the placeholder "applied an
    // effect", and 40 of the 63 operations never overrode it - so most of what the rule engine narrated
    // named the rule but not what it did.
    //
    // Two halves are pinned here:
    //   * the placeholder is gone and cannot come back (Describe() is abstract - a new operation does not
    //     compile without one), and the descriptions actually read as their sentence;
    //   * capabilities are ANSWERS, not events, and never reach the log at all.
    [TestFixture]
    public class RuleOperationDescribeTests
    {
        // --- capabilities are not events -------------------------------------------------------------

        [Test]
        public void CapabilityOperations_NeverReachTheLog()
        {
            var log = new CapturingLog();
            TestRuleHarness harness = HarnessLoggingTo(log,
                Confers("Bio-Gullet", new Effect.EnableTransport(new ValueSource.Literal(4))));
            IUnit unit = harness.BuildUnit("P1", 1, "Bio-Gullet");

            IReadOnlyList<RuleOperation> ops = harness.Evaluator.EvaluateAll(
                new CapabilityQueryContext(unit),
                RuleParticipant.Actor(unit, weapon: null, models: unit.Models));

            ops.HasOperation<RuleOperation.EnableTransport>();
            Assert.That(log.Lines, Is.Empty,
                "a capability is a question's answer - narrating it describes something that did not happen.");
        }

        [Test]
        public void CapabilitySuppression_IsByOperationType_NotByContext()
        {
            // The pre-existing guard skipped logging when the CONTEXT was a CapabilityQueryContext, which
            // holds only as long as every capability answer arrives through that one context. Fire the
            // same operation through an ordinary context: dropping by type is what keeps it quiet.
            var log = new CapturingLog();
            TestRuleHarness harness = HarnessLoggingTo(log,
                new SpecialRuleDefinition("Odd Caster",
                    new[]
                    {
                        new HookEntry(EHookID.Round_OnRoundStart, new Condition.Always(),
                            new Effect.EnableCasting(), ELifetime.UntilEndOfGame),
                    },
                    Array.Empty<ActivatedAbility>()));
            IUnit unit = harness.BuildUnit("P1", 1, "Odd Caster");

            IReadOnlyList<RuleOperation> ops = harness.Evaluator.EvaluateAll(
                new TestHookContext(EHookID.Round_OnRoundStart),
                RuleParticipant.Actor(unit, weapon: null, models: unit.Models));

            ops.HasOperation<RuleOperation.EnableCasting>();
            Assert.That(log.Lines, Is.Empty, "quiet because of what the operation IS, not where it came from.");
        }

        [Test]
        public void NonCapabilityOperations_StillLog()
        {
            var log = new CapturingLog();
            TestRuleHarness harness = HarnessLoggingTo(log,
                new SpecialRuleDefinition("Dread",
                    new[]
                    {
                        new HookEntry(EHookID.Round_OnRoundStart, new Condition.Always(),
                            new Effect.ExtraMeleeWoundCount(new ValueSource.Literal(2)),
                            ELifetime.UntilEndOfGame),
                    },
                    Array.Empty<ActivatedAbility>()));
            IUnit unit = harness.BuildUnit("P1", 1, "Dread");

            harness.Evaluator.EvaluateAll(new TestHookContext(EHookID.Round_OnRoundStart),
                RuleParticipant.Actor(unit, weapon: null, models: unit.Models));

            Assert.That(log.Lines, Has.Count.EqualTo(1));
            Assert.That(log.Lines[0], Is.EqualTo("P1-unit's Dread added 2 to its melee wound tally."),
                "the whole sentence: the evaluator supplies bearer + rule + full stop around Describe().");
        }

        // --- the descriptions themselves -------------------------------------------------------------

        [TestCaseSource(nameof(DescribedOperations))]
        public void Operation_DescribesWhatItDid(RuleOperation op, string expected)
        {
            Assert.That(op.Describe(), Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(DescribedOperations))]
        public void Description_IsAscii(RuleOperation op, string expected)
        {
            // The ImGui font atlas bakes Basic Latin + Latin-1 only; anything past U+00FF renders as '?'.
            Assert.That(op.Describe().Any(c => c > 0x7F), Is.False,
                $"non-ASCII in \"{op.Describe()}\" would print as '?' in game.");
        }

        [TestCaseSource(nameof(DescribedOperations))]
        public void Description_ReadsAsASentenceFragment(RuleOperation op, string expected)
        {
            string described = op.Describe();
            Assert.That(described, Is.Not.Empty);
            Assert.That(char.IsUpper(described[0]), Is.False,
                "the evaluator prefixes \"{bearer}'s {rule} \", so a leading capital reads as a new sentence.");
            Assert.That(described, Does.Not.EndWith("."), "the evaluator supplies the full stop.");
        }

        private static IEnumerable<TestCaseData> DescribedOperations()
        {
            yield return Case(new RuleOperation.StrikeFirst(), "struck first");
            yield return Case(new RuleOperation.StrikeLast(), "struck last");
            yield return Case(new RuleOperation.AllowShootAfterRush(), "could still shoot after Rushing");
            yield return Case(new RuleOperation.IgnoreEnemyMovementBlock(), "moved through enemy units");
            yield return Case(new RuleOperation.ExtraMeleeWoundCount(1), "added 1 to its melee wound tally");

            // Counts agree in number with what is printed. The float-carrying ops are fractional under
            // the probabilistic roller, so agreement follows the rendered value, not the raw number.
            yield return Case(new RuleOperation.InsertExtraHits(1f), "added 1 extra hit");
            yield return Case(new RuleOperation.InsertExtraHits(2f), "added 2 extra hits");
            yield return Case(new RuleOperation.InsertExtraHits(0.5f), "added 0.5 extra hits");
            yield return Case(new RuleOperation.InsertExtraWounds(1f), "added 1 extra wound");
            yield return Case(new RuleOperation.InflictSelfWounds(1f), "took 1 wound from its own attack");
            yield return Case(new RuleOperation.ReduceArmorPenetration(2), "reduced the attack's AP by 2");
            yield return Case(new RuleOperation.InflictSelfWounds(1.5f),
                "took 1.5 wounds from its own attack");
            yield return Case(new RuleOperation.RepositionModels(3f), "repositioned its models up to 3\"");
            yield return Case(new RuleOperation.PassMoraleTest(1),
                "passed a failed morale test, self-wounding on rolls of 1 or less");

            yield return Case(new RuleOperation.ChargeImpactHits(3), "rolled 3 impact dice on the charge");
            yield return Case(new RuleOperation.ChargeImpactHits(3, ArmorPenetration: 1),
                "rolled 3 impact dice on the charge at AP(1)");
            yield return Case(new RuleOperation.ChargeImpactHits(1), "rolled 1 impact die on the charge");
            // Counter cancels a charger's Impact dice through the same op, with a negative count.
            yield return Case(new RuleOperation.ChargeImpactHits(-1),
                "cancelled 1 impact die from the charge");
            yield return Case(new RuleOperation.ChargeImpactHits(-3),
                "cancelled 3 impact dice from the charge");

            // Both branches of each operation that reads differently per mode.
            yield return Case(new RuleOperation.IgnoreTerrainEffects(ETerrainIgnoreScope.DifficultOnly),
                "ignored difficult terrain");
            yield return Case(new RuleOperation.IgnoreTerrainEffects(ETerrainIgnoreScope.AllTerrain),
                "ignored all terrain effects");
            yield return Case(new RuleOperation.CountAsInTerrain(ECountAsTerrain.Dangerous),
                "counted as moving through Dangerous terrain");
            yield return Case(new RuleOperation.ApplyRangeModifier(6), "changed shooting range by +6\"");
            yield return Case(new RuleOperation.ApplyRangeModifier(-12, MinResultInches: 6),
                "changed shooting range by -12\" (no lower than 6\")");
            yield return Case(new RuleOperation.RestrictActions(new[] { EActionType.Hold }),
                "limited its actions to Hold");
            yield return Case(new RuleOperation.RestrictActions(Array.Empty<EActionType>()),
                "left it unable to act");
            yield return Case(
                new RuleOperation.DeferDeployment(EDeferTiming.AfterNormalDeployment,
                    PlacementRangeInches: 12f),
                "deployed after everything else, up to 12\" forward of its zone");
            yield return Case(
                new RuleOperation.DeferDeployment(EDeferTiming.LaterRound, PlacementRangeInches: 9f,
                    MinArrivalRound: 2),
                "was held in reserve, able to arrive from round 2 over 9\" from enemies");
            yield return Case(
                new RuleOperation.DeferDeployment(EDeferTiming.LaterRound, PlacementRangeInches: 9f,
                    MinArrivalRound: 2, MandatoryArrival: true),
                "was held in reserve, arriving from round 2 over 9\" from enemies");
        }

        private static TestCaseData Case(RuleOperation op, string expected) =>
            new TestCaseData(op, expected).SetArgDisplayNames(op.GetType().Name, expected);

        // --- descriptions that name another entity ---------------------------------------------------
        //
        // Kept out of the table above because they need a real IUnit: naming the OTHER unit is the whole
        // point of these lines (a cross-unit effect that says only what happened, not to whom, is the
        // ambiguity #197 called out on the token grants).

        [Test]
        public void DealHits_NamesTheTarget_AndOmitsEmptyApAndRules()
        {
            IUnit target = new TestRuleHarness().BuildUnit("P2", 1);

            Assert.That(new RuleOperation.InvokeDealHits(target, 3, Array.Empty<string>()).Describe(),
                Is.EqualTo("dealt 3 hits to P2-unit"));
            Assert.That(
                new RuleOperation.InvokeDealHits(target, 3, new[] { "Rending" }, ArmorPenetration: 2)
                    .Describe(),
                Is.EqualTo("dealt 3 hits to P2-unit at AP(2) with Rending"),
                "AP and carried rules appear only when they carry information.");
        }

        [Test]
        public void WeaponAttack_ReportsAMissingWeaponRatherThanGuessingOne()
        {
            IUnit target = new TestRuleHarness().BuildUnit("P2", 1);

            Assert.That(new RuleOperation.InvokeWeaponAttack(target, Weapon: null).Describe(),
                Is.EqualTo("had no weapon to attack P2-unit with"),
                "Weapon is null when the bearing rule was not weapon-scoped; the stage reports the same gap.");
        }

        [Test]
        public void DiceTallies_AgreeInNumber()
        {
            IUnit target = new TestRuleHarness().BuildUnit("P2", 1);

            Assert.That(new RuleOperation.InvokeDealAutoWounds(target, 1, 5).Describe(),
                Is.EqualTo("rolled 1 die at P2-unit, each 5+ a direct wound"));
            Assert.That(new RuleOperation.InvokeDealAutoWounds(target, 4, 5).Describe(),
                Is.EqualTo("rolled 4 dice at P2-unit, each 5+ a direct wound"),
                "\"dice\" is irregular, so it gets its own tally helper rather than RollTags.Count.");
        }

        [Test]
        public void Reactivate_MentionsClearedFatigueOnlyWhenItClearsIt()
        {
            IUnit unit = new TestRuleHarness().BuildUnit("P1", 1);

            Assert.That(new RuleOperation.InvokeReactivate(unit).Describe(),
                Is.EqualTo("gave P1-unit another activation"));
            Assert.That(new RuleOperation.InvokeReactivate(unit, ClearsFatigue: true).Describe(),
                Is.EqualTo("gave P1-unit another activation, clearing its fatigue"));
        }

        private static SpecialRuleDefinition Confers(string name, Effect capability) =>
            new(name,
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(), capability,
                        ELifetime.UntilEndOfGame),
                },
                Array.Empty<ActivatedAbility>());

        private static TestRuleHarness HarnessLoggingTo(ITextOutput log,
            params SpecialRuleDefinition[] definitions)
        {
            var harness = new TestRuleHarness(log: log);
            foreach (SpecialRuleDefinition definition in definitions) harness.Register(definition);
            return harness;
        }

        private sealed class CapturingLog : ITextOutput
        {
            public List<string> Lines { get; } = new();

            public void Log(string message, TextColor? color = null) => Lines.Add(message);

            // Trace lines ride the Debug channel; keep them out of the assertions above.
            public void LogDebug(string message, TextColor? color = null) { }
        }
    }
}
