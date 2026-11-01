using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    // #377 — the "redundant qualifier" phrase rules ("Bane when attacking", "Shred when attacking") are cataloged as
    // exact behavioral clones of their base rules: Bane and Shred only ever apply when attacking, so the qualifier
    // adds no gate (the #375 C9 "Counter in Melee" reading). #439: the third, "+6\" range when shooting", is gone -
    // the 3.6 parser grants Increased Shooting Range itself, so nothing reached the clone any more. These pins keep
    // the clones from drifting - if the base rule's entries change, the phrase rule must follow or consciously
    // diverge here.
    [TestFixture]
    public class SpellPhraseCloneTests
    {
        // #434: the "when attacking" pair consciously diverges on the CONDITION only - OPR 3.6 words the grant "gets Bane /
        // Shred ON ITS WEAPONS when attacking", and a caster's spell hits are not its weapons, so the clone gates on
        // IsNotSpell where the weapon-borne base rule (which a spell's own "with Bane" weapon carries) stays Always.
        private static readonly (SpecialRuleDefinition Clone, SpecialRuleDefinition Base, bool WeaponsOnly)[] Clones =
        {
            (CoreRuleCatalog.BaneWhenAttacking, CoreRuleCatalog.Bane, true),
            (CoreRuleCatalog.ShredWhenAttacking, CoreRuleCatalog.Shred, true),
        };

        [Test]
        public void PhraseClones_MatchTheirBaseRulesEntryForEntry()
        {
            foreach ((SpecialRuleDefinition clone, SpecialRuleDefinition baseRule, bool weaponsOnly) in Clones)
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
                    Assert.That(cloneEntry.Condition,
                        Is.EqualTo(weaponsOnly ? new Condition.IsNotSpell() : baseEntry.Condition),
                        $"'{clone.Name}' condition drifted from '{baseRule.Name}'.");
                }
            }
        }
    }
}
