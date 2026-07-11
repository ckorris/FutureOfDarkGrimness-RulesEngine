using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    // #166a — catalog-wide "every rule must fire" lint. One case per core rule: RuleFireLint proves
    // each passive entry can produce an operation in a synthesizable situation and each activated
    // ability is offered at a hook a stage polls and resolves to stage-executable operations. This is
    // the automated Breath Attack lesson (SpecialRulesAudit BUG-1): validation/registration/
    // serialization tests all pass for a rule that is a complete no-op in play; this suite cannot.
    //
    // Rules that legitimately fail live on the allowlist below WITH a reason — the allowlist is the
    // documented not-covered ledger. A stale entry (rule starts firing) fails the test too, so the
    // ledger can only shrink honestly.
    [TestFixture]
    public class RuleCatalogLintTests
    {
        private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>
        {
            ["Hero"] = "engine-marker: no dispatch entries; consumed by HeroJoinResolver/HeroStatRules " +
                "(join flow, stat substitution).",
            ["Transport"] = "engine-marker: no dispatch entries; capacity (EngineArgumentCount arg) is " +
                "read by TransportUtilities.GetCapacity, embark/disembark by their stages.",
            ["Limited"] = "engine-marker: once-per-game weapon gating is enforced by LimitedRules " +
                "reading the LimitedSpent token, not by dispatch entries.",
            ["Disembark"] = "stage-enacted: Effect.Disembark is a deliberate no-op marker; " +
                "ChooseActionStage routes the offer to DisembarkStage, which performs the move.",
            ["Embark"] = "stage-enacted: Effect.Embark is a deliberate no-op marker; ChooseActionStage " +
                "routes the offer to EmbarkStage after the spatial transport-in-range gate.",
            ["Teleport"] = "stage-enacted: Effect.Teleport is a deliberate no-op marker; ChooseActionStage " +
                "routes the offer to TeleportStage, which runs the 6\" placement (#197).",
        };

        // All plus the two standalone definitions FDGServer attaches to every unit at army setup —
        // they never appear in CoreRuleCatalog.All, but they are live in every game.
        private static IEnumerable<SpecialRuleDefinition> LintedRules() =>
            CoreRuleCatalog.All.Concat(new[] { CoreRuleCatalog.Disembark, CoreRuleCatalog.Embark });

        private static IEnumerable<TestCaseData> AllCatalogRules() =>
            LintedRules().Select(rule => new TestCaseData(rule).SetArgDisplayNames(rule.Name));

        [TestCaseSource(nameof(AllCatalogRules))]
        public void EveryCatalogRuleFires(SpecialRuleDefinition rule)
        {
            IReadOnlyList<string> problems = RuleFireLint.Check(rule);

            if (Allowlist.TryGetValue(rule.Name, out string? reason))
            {
                Assert.That(problems, Is.Not.Empty,
                    $"'{rule.Name}' is allowlisted ({reason}) but now passes the fire-lint - " +
                    "remove its stale allowlist entry.");
                return;
            }

            Assert.That(problems, Is.Empty,
                $"'{rule.Name}' fails the fire-lint:{Environment.NewLine}  " +
                string.Join($"{Environment.NewLine}  ", problems));
        }

        // The allowlist must never carry names the catalog doesn't — a renamed or deleted rule would
        // otherwise keep a phantom ledger entry forever.
        [Test]
        public void AllowlistNamesExistInCatalog()
        {
            var catalogNames = LintedRules().Select(r => r.Name).ToHashSet();
            var unknown = Allowlist.Keys.Where(name => !catalogNames.Contains(name)).ToList();
            Assert.That(unknown, Is.Empty,
                "Allowlist entries with no matching catalog rule: " + string.Join(", ", unknown));
        }
    }
}
