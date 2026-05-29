using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests.RulesHarness
{
    // NUnit-style helpers for asserting on the operation queue a hook produced, and on
    // the ability offers a hook surfaced.
    internal static class RuleAssertions
    {
        /// <summary> Asserts the queue contains at least one operation of type <typeparamref name="T"/>. </summary>
        public static void HasOperation<T>(this IReadOnlyList<RuleOperation> ops) where T : RuleOperation
        {
            Assert.That(ops.OfType<T>().Any(), Is.True,
                $"Expected at least one {typeof(T).Name} operation, found none.");
        }

        /// <summary>
        /// Asserts the queue contains at least one operation of type <typeparamref name="T"/>
        /// matching <paramref name="predicate"/>.
        /// </summary>
        public static void HasOperation<T>(this IReadOnlyList<RuleOperation> ops, Func<T, bool> predicate)
            where T : RuleOperation
        {
            Assert.That(ops.OfType<T>().Any(predicate), Is.True,
                $"Expected at least one {typeof(T).Name} operation matching the predicate, found none.");
        }

        /// <summary> Asserts an ability for <paramref name="ruleName"/> was offered. </summary>
        public static void HasOffer(this IReadOnlyList<AbilityOffer> offers, string ruleName)
        {
            Assert.That(offers.Any(o => o.RuleName == ruleName), Is.True,
                $"Expected an offer for '{ruleName}', found none.");
        }

        /// <summary>
        /// Asserts an ability for <paramref name="ruleName"/> was offered and matches
        /// <paramref name="predicate"/> (e.g. carries the expected cost).
        /// </summary>
        public static void HasOffer(this IReadOnlyList<AbilityOffer> offers, string ruleName,
            Func<AbilityOffer, bool> predicate)
        {
            Assert.That(offers.Any(o => o.RuleName == ruleName && predicate(o)), Is.True,
                $"Expected an offer for '{ruleName}' matching the predicate, found none.");
        }
    }
}
