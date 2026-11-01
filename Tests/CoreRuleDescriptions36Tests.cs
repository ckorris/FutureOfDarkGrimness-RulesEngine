using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    // #436 - description parity. Every catalog rule printed on the Core Rules v3.6.0 special-rules page carries that
    // page's sentence verbatim (ASCII-folded: curly quotes and inch marks become plain ones). These are the core rules
    // whose descriptions #436 rewrote from 3.5-era paraphrases; the others (Artillery, Indirect, Hero, Caster, Impact,
    // Counter, Parry, Transport, Unstoppable, ...) were already pinned by the items that re-authored them. The rule
    // glossary and hover text show these strings, so a drift here is a player-visible one. (The full sweep over the
    // catalog and both supplements is a scratchpad script, see WorkItems/436.)
    [TestFixture]
    public class CoreRuleDescriptions36Tests
    {
        [TestCase("Stealth", "When units where most models have this rule are shot from over 9\" away, enemy units get -1 to hit rolls.")]
        [TestCase("Reliable", "Attacks at Quality 2+.")]
        [TestCase("Fast", "Moves +2\" when using Advance, and +4\" when using Rush/Charge.")]
        [TestCase("Slow", "Moves -2\" when using Advance, and -4\" when using Rush/Charge.")]
        [TestCase("Surge", "For each unmodified result of 6 to hit, this weapon deals 1 extra hit (only the original hit counts as a 6 for special rules).")]
        [TestCase("Relentless", "When this model shoots at enemies over 9\" away, unmodified result of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).")]
        [TestCase("Furious", "When charging, unmodified result of 6 to hit deal 1 extra hit (only the original hit counts as a 6 for special rules).")]
        [TestCase("Deadly", "Assign each wound to one model, and multiply it by X. Hits from Deadly must be resolved first, and these wounds don't carry over to other models if the original target is killed.")]
        [TestCase("Regeneration", "When a unit where most models have this rule takes wounds, roll one die for each. On a 5+ it is ignored.")]
        [TestCase("Tough", "This model must take X wounds before being killed. If a model with Tough joins a unit without it, then it is removed last when the unit takes wounds. You must continue to put wounds on the Tough model with most wounds in the unit until it is killed, before starting to put them on the next Tough model (Heroes must be assigned wounds last, even if already wounded).")]
        [TestCase("Rending", "Ignores Regeneration, and on unmodified results of 6 to hit, those hits get AP(+4).")]
        [TestCase("Bane", "Ignores Regeneration, and when attacking, the target must re-roll unmodified Defense results of 6.")]
        [TestCase("Shred", "For each unmodified result of 1 to block hits, this weapon deals 1 extra wound.")]
        [TestCase("Thrust", "When charging, gets +1 to hit rolls and AP(+1) in melee.")]
        [TestCase("Blast", "Ignores Cover, and after resolving other special rules, each hit is multiplied by X, where X is up to as many hits as models in the target unit.")]
        [TestCase("Takedown", "This model may pick any model in the target unit as its individual target for this weapon's attacks, which is resolved as if it was a unit of [1]. Takedown attacks must be resolved before other weapons.")]
        [TestCase("Limited", "May only be used once per game.")]
        [TestCase("Fearless", "When a unit where most models have this rule fails a Morale Test, roll one die. On a 4+ it counts as passed instead.")]
        public void TheCatalogRule_CarriesTheCore360Sentence(string name, string text)
        {
            Assert.That(CoreRuleCatalog.CreateResolver().TryResolve(name, out ResolvedRule rule), Is.True, name);
            Assert.That(rule.Definition.Description, Is.EqualTo(text));
        }

        // The generic "X Aura" wording, 3.6's "get" (3.5 copies read "gain"): the books print "This model and its
        // unit get X." for every aura the catalog builds with the default text.
        [TestCase("Regeneration Aura", "Regeneration")]
        [TestCase("Stealth Aura", "Stealth")]
        [TestCase("Teleport Aura", "Teleport")]
        public void TheDefaultAuraText_IsThe36Wording(string aura, string granted)
        {
            Assert.That(CoreRuleCatalog.CreateResolver().TryResolve(aura, out ResolvedRule rule), Is.True, aura);
            Assert.That(rule.Definition.Description, Is.EqualTo($"This model and its unit get {granted}."));
        }
    }
}
