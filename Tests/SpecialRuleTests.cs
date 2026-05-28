using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class SpecialRuleTests
    {
        // Smoke test: the harness wires up and fires a hook end-to-end. With no
        // rules attached the stub bus returns an empty queue. Phase 6 adds the RED
        // tests that attach rules and expect non-empty results.
        [Test]
        public void HarnessFires_NoRules_ReturnsEmpty()
        {
            var harness = new TestRuleHarness();

            var operations = harness.Fire(new TestHookContext(EHookID.Round_OnRoundStart));

            Assert.That(operations, Is.Empty);
        }
    }
}
