using FDG.Rules.Definitions;
using NUnit.Framework;

namespace FDG.Tests.RulesHarness
{
    // NUnit-style helpers for asserting on the operation queue a hook produced.
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
    }
}
