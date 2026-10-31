using FDG.ArmyBuilding;
using NUnit.Framework;

namespace FDG.Tests;

// #437 - OPR 3.6 renamed five Grimdark Future books and four Age of Fantasy books (the Army Forge uids did not
// change). OprBookNames is the single rename table every name-keyed lookup goes through, so a renamed book keeps
// its curation (effect sets, overrides, rule-name fixes, bundled-book matching, the legacy-list backfill).
[TestFixture]
public class OprBookNamesTests
{
    [TestCase("Wormhole Daemons of Change", null, "Daemons of Change")]
    [TestCase("Wormhole Daemons of War", GameSystems.GrimdarkFuture, "Daemons of War")]
    [TestCase("Saurian Starhost", null, "Saurian Starhosts")]
    [TestCase("Rift Daemons of Plague", GameSystems.AgeOfFantasy, "Daemons of Plague")]
    [TestCase("rift daemons of lust", GameSystems.AgeOfFantasy, "Daemons of Lust")]
    public void Current_MapsA35NameToIts36Successor(string old, string? system, string expected)
    {
        Assert.That(OprBookNames.Current(old, system), Is.EqualTo(expected));
    }

    [TestCase("Daemons of Change", null)]
    [TestCase("Saurian Starhosts", null)]
    [TestCase("Battle Brothers", null)]
    [TestCase("Daemons of War", GameSystems.AgeOfFantasy)]
    [TestCase("Saurians", GameSystems.AgeOfFantasy)]
    [TestCase("A Hand-Authored Faction", null)]
    public void Current_LeavesCurrentAndUnknownNamesAlone(string name, string? system)
    {
        Assert.That(OprBookNames.Current(name, system), Is.EqualTo(name));
    }

    // The renames are per system: "Rift Daemons" was only ever AoF, "Wormhole Daemons" / "Saurian Starhost" only GDF.
    [Test]
    public void Current_IsPerGameSystem()
    {
        Assert.That(OprBookNames.Current("Rift Daemons of War", null), Is.EqualTo("Rift Daemons of War"));
        Assert.That(OprBookNames.Current("Wormhole Daemons of War", GameSystems.AgeOfFantasy), Is.EqualTo("Wormhole Daemons of War"));
        Assert.That(OprBookNames.Current(null, null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void SameBook_HoldsAcrossTheRename_ButNeverAcrossSystems()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OprBookNames.SameBook("Wormhole Daemons of War", null, "Daemons of War", GameSystems.GrimdarkFuture), Is.True);
            Assert.That(OprBookNames.SameBook("Daemons of War", GameSystems.AgeOfFantasy, "Rift Daemons of War", GameSystems.AgeOfFantasy), Is.True);
            Assert.That(OprBookNames.SameBook("SAURIAN STARHOST", null, "Saurian Starhosts", null), Is.True, "case-insensitive");
            // 3.6 gave BOTH systems a "Daemons of War" - different books.
            Assert.That(OprBookNames.SameBook("Daemons of War", null, "Daemons of War", GameSystems.AgeOfFantasy), Is.False);
            Assert.That(OprBookNames.SameBook("Wormhole Daemons of War", null, "Rift Daemons of War", GameSystems.AgeOfFantasy), Is.False);
            Assert.That(OprBookNames.SameBook("", null, "", null), Is.False, "empty names never match");
        });
    }

    [Test]
    public void Matches_ReadsTheBooksFactionOrName()
    {
        var bundled35 = new BookFile { Name = "Rift Daemons of Change", Faction = "Rift Daemons of Change", GameSystem = GameSystems.AgeOfFantasy };
        Assert.That(OprBookNames.Matches(bundled35, "Daemons of Change", GameSystems.AgeOfFantasy), Is.True, "a 3.6 list, a 3.5 bundled book");
        Assert.That(OprBookNames.Matches(bundled35, "Daemons of Change", null), Is.False, "a GDF list never takes the AoF book");

        var bundled36 = new BookFile { Name = "Saurian Starhosts", Faction = "Saurian Starhosts" };
        Assert.That(OprBookNames.Matches(bundled36, "Saurian Starhost", null), Is.True, "a 3.5 army, a 3.6 bundled book");
        Assert.That(OprBookNames.Matches(new BookFile { Name = "Custom", Faction = "" }, "Custom", null), Is.True, "Name fallback");
    }
}
