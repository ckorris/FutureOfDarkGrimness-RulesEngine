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

    // NOTE (2026-07-19): a share list's per-unit `cost` is the unit's BASE cost and `listPoints` is the
    // only resolved total - verified against OPR's own book API, where the same per-unit figure appears as
    // the catalog price. These fixtures originally encoded `cost` as resolved (base + upgrades), which is
    // why the reconciliation was comparing incomparable numbers. Pass base costs here.
    private static string ListJson(int? listPoints, params string[] units) => $$"""
        { "name": "Recon Test", "gameSystem": "gf", "pointsLimit": 500,
          {{(listPoints is null ? "" : $"\"listPoints\": {listPoints},")}}
          "units": [ {{string.Join(",\n", units)}} ] }
        """;

    [Test]
    public void Reconstruct_RebuildsUnits_Links_AndChoices_WithMatchingPoints()
    {
        string json = ListJson(320,
            UnitJson("hero1", "Champion", 145, "SelHero", joinToUnit: "SelGrunts"),
            UnitJson("grunts", "Grunts", 160, "SelGrunts",
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

        // Base costs agree unit for unit (145 vs 145, 160 vs 160), and our compiled 145+160+15 lands on
        // Army Forge's own listPoints of 320: the reconciliation is clean on both axes.
        Assert.That(result.UnitPointsDeltas, Is.Empty);
        Assert.That(result.OurTotalPoints, Is.EqualTo(320));
        Assert.That(result.TheirTotalPoints, Is.EqualTo(320));
        Assert.That(result.TheirTotalIsAuthoritative, Is.True);
        Assert.That(result.ExcludedUnits, Is.Empty);
    }

    [Test]
    public void Reconstruct_ExcludesUnknownUnit_AndCleansLinksIntoIt()
    {
        string json = ListJson(245,
            UnitJson("nope", "Ghost Walker", 100, "SelGhost"),
            UnitJson("hero1", "Champion", 145, "SelHero", joinToUnit: "SelGhost"));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.ExcludedUnits, Is.EqualTo(new[] { ("Ghost Walker", 100) }));
        Assert.That(result.Warnings, Has.Some.Contains("excluded (100 pts)"));

        BuilderUnit hero = result.Selections.Units.Single();
        Assert.That(hero.JoinsUnitId, Is.Null, "a join into an excluded unit must not dangle");

        // Army Forge's total still counts the excluded unit and we cannot back its share out exactly, so
        // the two totals are NOT comparable here - the gap must be disclosed, not presented as a delta.
        Assert.That(result.OurTotalPoints, Is.EqualTo(145));
        Assert.That(result.TheirTotalPoints, Is.EqualTo(245));
        Assert.That(result.Warnings, Has.Some.Contains("not directly comparable"));
    }

    [Test]
    public void Reconstruct_DropsUnmatchableUpgrade_AndReportsPointsDelta()
    {
        string json = ListJson(175,
            UnitJson("grunts", "Grunts", 160, "SelGrunts",
                selectedUpgrades: """[ { "option": { "uid": "ZZZ", "label": "Void Cannon" } } ]"""));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.Selections.Units.Single().Choices, Is.Empty);
        Assert.That(result.Warnings, Has.Some.Contains("Void Cannon"));

        // The BASE costs agree (160 vs 160) - a dropped upgrade cannot show up as a per-unit delta, because
        // Army Forge never publishes a resolved per-unit cost to subtract from. The 15-point gap surfaces
        // in the totals instead, alongside the dropped-upgrade warning.
        Assert.That(result.UnitPointsDeltas, Is.Empty);
        Assert.That(result.OurTotalPoints, Is.EqualTo(160));
        Assert.That(result.TheirTotalPoints, Is.EqualTo(175));
    }

    [Test]
    public void Reconstruct_MatchesOptionByLabel_WhenIdsAreAbsent()
    {
        string json = ListJson(175,
            UnitJson("grunts", "Grunts", 160, "SelGrunts",
                selectedUpgrades: """[ { "option": { "label": "banner" } } ]"""));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.Selections.Units.Single().Choices.Single().OptionId, Is.EqualTo("O1"));
        Assert.That(result.UnitPointsDeltas, Is.Empty);
    }

    // Regression (2026-07-19): the fix that started this - an unpriced upgrade must not be reported as our
    // compiler getting the price wrong. OPR omits `cost` on options it prices internally; we count them as
    // free because no endpoint publishes the number, and we SAY so.
    [Test]
    public void Reconstruct_DisclosesUnpricedUpgrades_RatherThanBlamingOurCompiler()
    {
        BookFile book = MakeBook();
        UpgradeOption banner = book.Units.Single(u => u.Id == "grunts").Sections.Single().Options.Single();
        banner.Cost = 0;
        banner.CostUnpriced = true;   // as OprBookImporter marks an option whose `cost` key is absent

        string json = ListJson(175,
            UnitJson("grunts", "Grunts", 160, "SelGrunts",
                selectedUpgrades: """[ { "upgrade": { "uid": "S1" }, "option": { "uid": "O1", "label": "Banner" } } ]"""));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, book);

        Assert.That(result.UnpricedUpgradeCount, Is.EqualTo(1));
        Assert.That(result.Warnings, Has.Some.Contains("no published Army Forge price"));
        // Base costs still agree; the 15-point shortfall is OPR's unpublished price, not our arithmetic.
        Assert.That(result.UnitPointsDeltas, Is.Empty);
        Assert.That(result.OurTotalPoints, Is.EqualTo(160));
        Assert.That(result.TheirTotalPoints, Is.EqualTo(175));
    }

    // A base-cost disagreement is the one per-unit comparison that IS valid, and it means the bundled book
    // has drifted from OPR's catalog - the check must survive the base-vs-base correction.
    [Test]
    public void Reconstruct_ReportsBaseCostDrift_BetweenBundledBookAndShareList()
    {
        string json = ListJson(150, UnitJson("grunts", "Grunts", 150, "SelGrunts"));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.UnitPointsDeltas, Is.EqualTo(new[] { ("Grunts", 160, 150) }));
    }

    [Test]
    public void Reconstruct_FallsBackToBaseSum_WhenListPointsIsAbsent()
    {
        string json = ListJson(null, UnitJson("hero1", "Champion", 145, "SelHero"));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, MakeBook());

        Assert.That(result.TheirTotalPoints, Is.EqualTo(145));
        Assert.That(result.TheirTotalIsAuthoritative, Is.False);
    }

    // #323, the import half of the report: a share list carrying TWO swaps out of a section whose second
    // target another section grants ("Replace Titan Shield" -> a second Heavy Hammer) rebuilt both choices
    // correctly, but the compile behind it dropped one - so the imported list quietly lost an upgrade and
    // read light on points. Both selections must survive the round trip into the Forge session.
    [Test]
    public void Reconstruct_KeepsBothSwaps_WhenTheSecondTargetComesFromAnotherSection()
    {
        var titan = new RosterUnit
        {
            Id = "titan", Name = "Errant Mini-Titan", Quality = 3, Defense = 2,
            BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 295,
            Weapons = { new WeaponFileEntry { Name = "Heavy Hammer", Quantity = 1, Attacks = 2 } },
            Items = { new ItemEntry { Name = "Titan Shield", Quantity = 1 } },
            Sections =
            {
                new UpgradeSection
                {
                    Id = "HAM", Label = "Replace any Heavy Hammer", Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.Any, Targets = { "Heavy Hammer" },
                    Options = { new UpgradeOption { Id = "SWD", Label = "Heavy Sword", Cost = 30,
                        WeaponsGained = { new WeaponFileEntry { Name = "Heavy Sword", Quantity = 1, Attacks = 6 } } } },
                },
                new UpgradeSection
                {
                    Id = "SHD", Label = "Replace Titan Shield", Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.One, Targets = { "Titan Shield" },
                    Options = { new UpgradeOption { Id = "HMR", Label = "Heavy Hammer", Cost = 30,
                        WeaponsGained = { new WeaponFileEntry { Name = "Heavy Hammer", Quantity = 1, Attacks = 2 } } } },
                },
            },
        };
        var book = new BookFile { Name = "Titan Lords", Faction = "Titan Lords", Units = { titan } };

        // Army Forge emits one selectedUpgrades entry per application - two for the two hammer swaps.
        string json = ListJson(385, UnitJson("titan", "Errant Mini-Titan", 295, "SelTitan", selectedUpgrades: """
            [ { "upgrade": { "uid": "SHD" }, "option": { "uid": "HMR", "label": "Heavy Hammer" } },
              { "upgrade": { "uid": "HAM" }, "option": { "uid": "SWD", "label": "Heavy Sword" } },
              { "upgrade": { "uid": "HAM" }, "option": { "uid": "SWD", "label": "Heavy Sword" } } ]
            """));

        OprForgeSessionResult result = OprListImporter.ReconstructSelections(json, book);

        BuilderUnit rebuilt = result.Selections.Units.Single();
        Assert.That(rebuilt.Choices.Single(c => c.SectionId == "HAM").Count, Is.EqualTo(2),
            "both hammer swaps come across as applications of one choice");

        UnitFileEntry compiled = ListCompiler.Compile(book, result.Selections).Units.Single();
        Assert.That(compiled.Weapons.Single(w => w.Name == "Heavy Sword").Quantity, Is.EqualTo(2),
            "and both survive the compile - neither hammer is left behind");
        Assert.That(result.OurTotalPoints, Is.EqualTo(385), "matching Army Forge's own listPoints");
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void Reconstruct_LinksCombinedPair_AndCompileMergesThem()
    {
        string json = ListJson(320,
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
