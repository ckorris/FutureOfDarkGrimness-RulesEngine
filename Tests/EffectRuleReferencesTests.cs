using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #369 — which rules an effect NAMES, so an ability's description can explain the buff it confers.
    // Derived from the effect rather than by scanning the description for rule-looking words: half the
    // catalog's names are ordinary English ("Fast", "Tough", "Devout"), and a text scan would underline
    // prose.
    [TestFixture]
    public class EffectRuleReferencesTests
    {
        [Test]
        public void AddRule_Aura_MarkTarget_AndIgnoreRule_EachNameTheirRule()
        {
            Assert.That(EffectRuleReferences.NamesIn(new Effect.AddRule("Courage", ELifetime.NextTrigger)),
                Is.EqualTo(new[] { "Courage" }));
            Assert.That(EffectRuleReferences.NamesIn(new Effect.Aura("Regeneration")),
                Is.EqualTo(new[] { "Regeneration" }));
            Assert.That(EffectRuleReferences.NamesIn(new Effect.MarkTarget("Bane")),
                Is.EqualTo(new[] { "Bane" }));
            Assert.That(EffectRuleReferences.NamesIn(new Effect.IgnoreRule("Regeneration")),
                Is.EqualTo(new[] { "Regeneration" }));
        }

        // A spell-shaped effect attaches several weapon rules to the hits it deals; all of them are worth
        // explaining, in the order authored.
        [Test]
        public void DealHits_NamesEveryRuleItAttaches_InOrder()
        {
            var effect = new Effect.DealHits(3, new List<string> { "Blast(3)", "Bane" });

            Assert.That(EffectRuleReferences.NamesIn(effect),
                Is.EqualTo(new[] { "Blast(3)", "Bane" }));
        }

        // MoraleTestThen wraps another effect - the only effect that does - so the rules its failure arm
        // names must not be lost.
        [Test]
        public void MoraleTestThen_ReachesTheEffectItWraps()
        {
            var effect = new Effect.MoraleTestThen(new Effect.AddRule("Guarded", ELifetime.ThisRound));

            Assert.That(EffectRuleReferences.NamesIn(effect), Is.EqualTo(new[] { "Guarded" }));
        }

        [Test]
        public void AnEffectThatNamesNoRule_ReturnsNothing()
        {
            Assert.That(EffectRuleReferences.NamesIn(new Effect.QualityFloor(2)), Is.Empty);
            Assert.That(EffectRuleReferences.NamesIn(null), Is.Empty);
        }
    }
}
