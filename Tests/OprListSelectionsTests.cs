using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #241 v2 — share list -> editable BuilderList against the bundled book ("Open in Forge"), plus the
// points reconciliation that doubles as a #218/#219 pricing check. Synthetic book + list (our own data),
// ids shaped like the corpus: book RosterUnit.Id == list unit `id`, section/option ids preserved.
[TestFixture]
public class OprListSelectionsTests
{
    private static BookFile MakeBook()
    {
        var book = new BookFile { Name = "Legion Alpha", Faction = "Legion Alpha" };
        book.Units.Add(new RosterUnit
        {
            Id = "hero1", Name = "Champion", Quality = 3, Defense = 3,
            BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 145,
            Weapons = { new WeaponFileEntry { Name = "Champion Rifle", Quantity = 1, RangeInches = 24, Attacks = 2 } },
            Rules = { new SpecialRuleEntry_Core("Hero") },
        });
        var grunts = new RosterUnit
        {
            Id = "grunts", Name = "Grunts", Quality = 4, Defense = 4,
            BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 160,
            Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 5, RangeInches = 24, Attacks = 1 } },
        };
        grunts.Sections.Add(new UpgradeSection
        {
            Id = "S1", Label = "Upgrade with:", Variant = UpgradeVariant.Upgrade, Affects = UpgradeAffects.One,
            Options = { new UpgradeOption { Id = "O1", Label = "Banner", Cost = 15 } },
        });
        book.Units.Add(grunts);
        return book;
    }

    private static string UnitJson(string id, string name, int cost, string selectionId,
        string? joinToUnit = null, bool combined = false, string selectedUpgrades = "[]") => $$"""
        { "id": "{{id}}", "name": "{{name}}", "cost": {{cost}}, "size": 1, "quality": 4, "defense": 4,
          "armyId": "bookA", "combined": {{(combined ? "true" : "false")}},
          "joinToUnit": {{(joinToUnit is null ? "null" : $"\"{joinToUnit}\"")}},
          "selectionId": "{{selectionId}}", "selectedUpgrades": {{selectedUpgrades}}, "loadout": [] }
        """;

    private static string ListJson(params string[] units) => $$"""
        { "name": "Recon Test", "gameSystem": "gf", "pointsLimit": 500,
          "units": [ {{string.Join(",\n", units)}} ] }
        """;

    [Test]
    public void Reconstruct_RebuildsUnits_Links_AndChoices_WithMatchingPoints()
    {
        string json = ListJson(
            UnitJson("hero1", "Champion", 145, "SelHero", joinToUnit: "SelGrunts"),
            UnitJson("grunts", "Grunts", 175, "SelGrunts",
                selectedUpgrades: """[ { "upgrade": { "uid": "S1" }, "option": { "uid": "O1", "label": "Banner" } } ]"""));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.Selections.Units, Has.Count.EqualTo(2));
        Assert.That(result.Selections.BookName, Is.EqualTo("Legion Alpha"));

        BuilderUnit hero = result.Selections.Units[0];
        Assert.That(hero.RosterUnitId, Is.EqualTo("hero1"));
        Assert.That(hero.JoinsUnitId, Is.EqualTo("SelGrunts"));

        BuilderUnit grunts = result.Selections.Units[1];
        Assert.That(grunts.Choices, Has.Count.EqualTo(1));
        Assert.That(grunts.Choices[0].SectionId, Is.EqualTo("S1"));
        Assert.That(grunts.Choices[0].OptionId, Is.EqualTo("O1"));

        // 145 + 160 + 15 = 320 on both sides: the reconciliation is clean.
        Assert.That(result.UnitPointsDeltas, Is.Empty);
        Assert.That(result.OurTotalPoints, Is.EqualTo(320));
        Assert.That(result.TheirTotalPoints, Is.EqualTo(320));
        Assert.That(result.ExcludedUnits, Is.Empty);
    }

    [Test]
    public void Reconstruct_ExcludesUnknownUnit_AndCleansLinksIntoIt()
    {
        string json = ListJson(
            UnitJson("nope", "Ghost Walker", 100, "SelGhost"),
            UnitJson("hero1", "Champion", 145, "SelHero", joinToUnit: "SelGhost"));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.ExcludedUnits, Is.EqualTo(new[] { ("Ghost Walker", 100) }));
        Assert.That(result.Warnings, Has.Some.Contains("excluded (100 pts)"));

        BuilderUnit hero = result.Selections.Units.Single();
        Assert.That(hero.JoinsUnitId, Is.Null, "a join into an excluded unit must not dangle");

        // The excluded unit is out of BOTH totals, so the comparison stays fair.
        Assert.That(result.TheirTotalPoints, Is.EqualTo(145));
        Assert.That(result.OurTotalPoints, Is.EqualTo(145));
    }

    [Test]
    public void Reconstruct_DropsUnmatchableUpgrade_AndReportsPointsDelta()
    {
        string json = ListJson(
            UnitJson("grunts", "Grunts", 175, "SelGrunts",
                selectedUpgrades: """[ { "option": { "uid": "ZZZ", "label": "Void Cannon" } } ]"""));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.Selections.Units.Single().Choices, Is.Empty);
        Assert.That(result.Warnings, Has.Some.Contains("Void Cannon"));

        // Army Forge priced the unit with the upgrade (175); without it we compile 160 - disclosed, not hidden.
        Assert.That(result.UnitPointsDeltas, Is.EqualTo(new[] { ("Grunts", 160, 175) }));
        Assert.That(result.OurTotalPoints, Is.EqualTo(160));
        Assert.That(result.TheirTotalPoints, Is.EqualTo(175));
    }

    [Test]
    public void Reconstruct_MatchesOptionByLabel_WhenIdsAreAbsent()
    {
        string json = ListJson(
            UnitJson("grunts", "Grunts", 175, "SelGrunts",
                selectedUpgrades: """[ { "option": { "label": "banner" } } ]"""));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.Selections.Units.Single().Choices.Single().OptionId, Is.EqualTo("O1"));
        Assert.That(result.UnitPointsDeltas, Is.Empty);
    }

    [Test]
    public void Reconstruct_LinksCombinedPair_AndCompileMergesThem()
    {
        string json = ListJson(
            UnitJson("grunts", "Grunts", 160, "SelA", combined: true),
            UnitJson("grunts", "Grunts", 160, "SelB", joinToUnit: "SelA", combined: true));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.Selections.Units[1].CombinedWithId, Is.EqualTo("SelA"));
        Assert.That(result.Selections.Units[1].JoinsUnitId, Is.Null, "combined linkage is not a hero join");
        Assert.That(result.UnitPointsDeltas, Is.Empty);
        Assert.That(result.OurTotalPoints, Is.EqualTo(320));

        // The session compiles the same way the Forge screen will show it: one merged unit.
        BuiltArmyFile compiled = ListCompiler.Compile(MakeBook(), result.Selections);
        Assert.That(compiled.Units, Has.Count.EqualTo(1));
        Assert.That(compiled.Units[0].ModelCount, Is.EqualTo(10));
    }
}
