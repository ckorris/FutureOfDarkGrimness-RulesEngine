using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    // #377 — the "redundant qualifier" phrase rules ("Bane when attacking", "Shred when attacking",
    // "+6\" range when shooting") are cataloged as exact behavioral clones of their base rules: Bane and
    // Shred only ever apply when attacking, and range checks only exist for shooting, so the qualifier
    // adds no gate (the #375 C9 "Counter in Melee" reading). These pins keep the clones from drifting -
    // if the base rule's entries change, the phrase rule must follow or consciously diverge here.
    [TestFixture]
    public class SpellPhraseCloneTests
    {
        private static readonly (SpecialRuleDefinition Clone, SpecialRuleDefinition Base)[] Clones =
        {
            (CoreRuleCatalog.BaneWhenAttacking, CoreRuleCatalog.Bane),
            (CoreRuleCatalog.ShredWhenAttacking, CoreRuleCatalog.Shred),
            (CoreRuleCatalog.RangeBonusWhenShooting, CoreRuleCatalog.IncreasedShootingRange),
        };

        [Test]
        public void PhraseClones_MatchTheirBaseRulesEntryForEntry()
        {
            foreach ((SpecialRuleDefinition clone, SpecialRuleDefinition baseRule) in Clones)
            {
                Assert.That(clone.Passive.Count, Is.EqualTo(baseRule.Passive.Count),
                    $"'{clone.Name}' must carry the same entry count as '{baseRule.Name}'.");
                foreach ((HookEntry cloneEntry, HookEntry baseEntry) in clone.Passive.Zip(baseRule.Passive))
                {
                    Assert.That(cloneEntry.HookID, Is.EqualTo(baseEntry.HookID),
                        $"'{clone.Name}' hook drifted from '{baseRule.Name}'.");
                    Assert.That(cloneEntry.Seat, Is.EqualTo(baseEntry.Seat),
                        $"'{clone.Name}' seat drifted from '{baseRule.Name}'.");
                    Assert.That(cloneEntry.Effect, Is.EqualTo(baseEntry.Effect),
                        $"'{clone.Name}' effect drifted from '{baseRule.Name}'.");
                    Assert.That(cloneEntry.Condition, Is.EqualTo(baseEntry.Condition),
                        $"'{clone.Name}' condition drifted from '{baseRule.Name}'.");
                }
            }
        }
    }
}
