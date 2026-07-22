using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #241 — the OPR Army Forge share-list JSON (/api/tts) → ArmyListFile mapping, on a small synthetic list
// (our own, not OPR data) shaped like the real corpus: a hero joined to a combined pair, final `loadout`
// as the gear source, AP folded into ArmorPenetration, item rules folded into the unit, selectedUpgrade
// rule gains dedupe-merged, and the version / game-system gates.
[TestFixture]
public class OprListImporterTests
{
    // Mirrors the real /api/tts shape. Deliberate details: the list name carries an em-dash (must fold to
    // ASCII); "Tough":"3" is a string (OPR types ratings loosely); the combined pair shares unit id "grunts"
    // with the absorbed half's joinToUnit naming the host's selectionId; the hero joins the HOST half; the
    // support unit comes from a second army book and carries campaign xp/traits.
    private const string ListJson = """
    {
      "id": "TESTLIST", "name": "Test — Strike Force", "gameSystem": "gf", "pointsLimit": 500,
      "campaignMode": false, "narrativeMode": false, "listPoints": 515,
      "units": [
        { "id": "hero1", "cost": 145, "name": "Champion", "size": 1, "bases": { "round": "40", "square": "40" },
          "rules": [ {"name":"Hero"}, {"name":"Tough","rating":6} ],
          "quality": 3, "defense": 3,
          "weapons": [],
          "armyId": "bookA", "xp": 0, "traits": [], "combined": false,
          "joinToUnit": "SelHostA", "selectionId": "SelHero", "selectedUpgrades": [],
          "loadout": [
            { "type": "ArmyBookWeapon", "name": "Champion Rifle", "count": 1, "range": 24, "attacks": 2,
              "attacksMultiplier": 1, "specialRules": [ {"name":"AP","rating":1} ] },
            { "type": "ArmyBookWeapon", "name": "CCW", "count": 1, "range": 0, "attacks": 4, "specialRules": [] }
          ] },
        { "id": "grunts", "cost": 160, "name": "Grunts", "size": 5, "bases": { "round": "32", "square": "30" },
          "rules": [ {"name":"Strider"} ], "quality": 4, "defense": 4,
          "armyId": "bookA", "combined": true, "joinToUnit": null, "selectionId": "SelHostA",
          "loadout": [
            { "type": "ArmyBookWeapon", "name": "Rifle", "count": 5, "range": 24, "attacks": 1,
              "specialRules": [ {"name":"AP","rating":1} ] },
            { "type": "ArmyBookWeapon", "name": "CCW", "count": 5, "range": 0, "attacks": 1, "specialRules": [] }
          ] },
        { "id": "grunts", "cost": 160, "name": "Grunts", "size": 5, "bases": { "round": "32", "square": "30" },
          "rules": [ {"name":"Strider"} ], "quality": 4, "defense": 4,
          "armyId": "bookA", "combined": true, "joinToUnit": "SelHostA", "selectionId": "SelHalfB",
          "loadout": [
            { "type": "ArmyBookWeapon", "name": "Rifle", "count": 5, "range": 24, "attacks": 1,
              "specialRules": [ {"name":"AP","rating":1} ] },
            { "type": "ArmyBookWeapon", "name": "CCW", "count": 5, "range": 0, "attacks": 1, "specialRules": [] }
          ] },
        { "id": "sup1", "cost": 50, "name": "Support", "size": 3, "bases": { "round": "60x35" },
          "rules": [ {"name":"Tough","rating":"3"} ], "quality": 5, "defense": 5,
          "armyId": "bookB", "xp": 2, "traits": ["Agile"], "combined": false,
          "joinToUnit": null, "selectionId": "SelSup",
          "selectedUpgrades": [
            { "option": { "gains": [ {"type":"ArmyBookRule","name":"Ambush"} ] } },
            { "option": { "gains": [ {"type":"ArmyBookRule","name":"Ambush"} ] } }
          ],
          "loadout": [
            { "type": "ArmyBookWeapon", "name": "Launcher", "count": 3, "range": 18, "attacks": 1, "specialRules": [] },
            { "type": "ArmyBookItem", "name": "Shield Gear", "count": 3,
              "content": [ {"type":"ArmyBookRule","name":"Shield Wall"} ] }
          ] }
      ],
      "forceOrgErrors": [ "Points limit exceeded: 515/500" ]
    }
    """;

    private static readonly Dictionary<string, string> Books = new()
    {
        ["bookA"] = """{"uid":"bookA","name":"Legion Alpha","versionString":"3.5.3","units":[]}""",
        ["bookB"] = """{"uid":"bookB","name":"Legion Beta","versionString":"3.5.2","units":[]}""",
    };

    private static OprListImportResult Import() => OprListImporter.Import(ListJson, Books);

    // Regression (2026-07-19, #241). Per-unit `cost` is a BASE cost and `listPoints` is the only resolved
    // total, so summing the units imported every upgraded list LIGHT by exactly its upgrade points (150 on
    // the real High Elf Fleets list that surfaced this). The gap is unattributable by construction - OPR
    // publishes no per-option price for the upgrades that make it up - so it is parked on the army whole
    // rather than split on a guess. 490 base + a 60-pt gap against a 500-pt limit: light it reads legal,
    // correct it does not.
    private const string GapListJson = """
    {
      "id": "GAPLIST", "name": "Upgraded", "gameSystem": "gf", "pointsLimit": 500,
      "campaignMode": false, "narrativeMode": false, "listPoints": 550,
      "units": [
        { "id": "hero1", "cost": 145, "name": "Champion", "size": 1, "quality": 3, "defense": 3,
          "rules": [], "weapons": [], "armyId": "bookA", "combined": false,
          "selectionId": "SelHero", "selectedUpgrades": [], "loadout": [] },
        { "id": "grunts", "cost": 345, "name": "Grunts", "size": 5, "quality": 4, "defense": 4,
          "rules": [], "weapons": [], "armyId": "bookA", "combined": false,
          "selectionId": "SelGrunts", "selectedUpgrades": [], "loadout": [] }
      ]
    }
    """;

    [Test]
    public void Import_TotalsFromListPoints_ParkingUpgradePointsItCannotAttribute()
    {
        ArmyListFile army = OprListImporter.Import(GapListJson, Books).Army;

        Assert.That(army.Units.Sum(u => u.PointCost), Is.EqualTo(490), "BASE costs: 145 + 345");
        Assert.That(army.UnattributedPoints, Is.EqualTo(60), "550 listPoints - 490 attributed");
        Assert.That(army.TotalPoints, Is.EqualTo(550), "the army must total what Army Forge says it totals");
    }

    [Test]
    public void Import_WarnsWhenUpgradePointsCannotBeAttributed()
    {
        // GapListJson's units carry no selectedUpgrades, so the 60-pt gap is genuinely unattributable.
        Assert.That(OprListImporter.Import(GapListJson, Books).Warnings,
            Has.Some.Contains("could not be attributed to a specific unit"));
    }

    // #219: a selection carries its price (a per-unit `costs` entry keyed by unit id), so the unit's upgrade
    // points attribute to it - the unit reads base + upgrades and nothing is left unattributed.
    private const string AttributedListJson = """
    {
      "id": "ATTRLIST", "name": "Noble", "gameSystem": "gf", "pointsLimit": 2000,
      "campaignMode": false, "narrativeMode": false, "listPoints": 120,
      "units": [
        { "id": "noble", "cost": 45, "name": "Elven Noble", "size": 1, "quality": 3, "defense": 4,
          "rules": [], "weapons": [], "armyId": "bookA", "combined": false,
          "selectionId": "SelNoble", "loadout": [],
          "selectedUpgrades": [
            { "option": { "id": "o1", "label": "Master Shard Pistol",
                "costs": [ {"cost":10,"unitId":"noble"}, {"cost":5,"unitId":"other"} ] } },
            { "option": { "id": "o2", "label": "Master Laser Pistol", "costs": [ {"cost":5,"unitId":"noble"} ] } },
            { "option": { "id": "o3", "label": "Shield Projector", "costs": [ {"cost":30,"unitId":"noble"} ] } },
            { "option": { "id": "o4", "label": "Elemental Hexer", "costs": [ {"cost":30,"unitId":"noble"} ] } }
          ] }
      ]
    }
    """;

    [Test]
    public void Import_AttributesPerUnitUpgradeCosts_LeavingNothingUnattributed()
    {
        ArmyListFile army = OprListImporter.Import(AttributedListJson, Books).Army;

        // 45 base + 10 + 5 + 30 + 30 = 120, using THIS unit's costs[] entry (10, not the "other" unit's 5).
        Assert.That(army.Units.Single().PointCost, Is.EqualTo(120));
        Assert.That(army.UnattributedPoints, Is.Zero);
        Assert.That(army.TotalPoints, Is.EqualTo(120));
    }

    // Force-org validation reads TotalPoints; importing light let an over-limit list pass as legal.
    [Test]
    public void ImportedArmy_OverPointsLimit_IsCaughtByForceOrgValidation()
    {
        ArmyListFile army = OprListImporter.Import(GapListJson, Books).Army;

        Assert.That(army.Units.Sum(u => u.PointCost), Is.LessThan(army.PointsLimit),
            "base costs alone are UNDER the limit - which is exactly how the bug hid");
        Assert.That(ForceOrgValidator.Validate(army), Has.Some.Contains("550 / 500"));
    }

    // A list whose units already sum to its listPoints (no priced upgrades) must park nothing.
    [Test]
    public void Import_ParksNothing_WhenUnitCostsAlreadyMatchListPoints()
    {
        ArmyListFile army = Import().Army;

        Assert.That(army.Units.Sum(u => u.PointCost), Is.EqualTo(515));
        Assert.That(army.UnattributedPoints, Is.Zero);
        Assert.That(army.TotalPoints, Is.EqualTo(515));
    }

    [Test]
    public void Peek_ExtractsNameSystemAndArmyIds()
    {
        OprListHeader header = OprListImporter.Peek(ListJson);
        Assert.That(header.Name, Is.EqualTo("Test - Strike Force")); // Peek ascii-folds the display name
        Assert.That(header.GameSystem, Is.EqualTo("gf"));
        Assert.That(header.ArmyIds, Is.EqualTo(new[] { "bookA", "bookB" }));
    }

    [Test]
    public void Import_MapsListAndUnitBasics_AndFoldsAscii()
    {
        OprListImportResult result = Import();
        ArmyListFile army = result.Army;

        Assert.That(army.Name, Is.EqualTo("Test - Strike Force")); // em-dash folded
        Assert.That(army.PointsLimit, Is.EqualTo(500));
        Assert.That(army.Faction, Is.EqualTo("Legion Alpha"));     // majority book
        Assert.That(army.Units, Has.Count.EqualTo(3));             // hero + merged grunts + support

        UnitFileEntry hero = army.Units.Single(u => u.Name == "Champion");
        Assert.That(hero.Quality, Is.EqualTo(3));
        Assert.That(hero.Defense, Is.EqualTo(3));
        Assert.That(hero.PointCost, Is.EqualTo(145));
        Assert.That(hero.ModelCount, Is.EqualTo(1));
        Assert.That(hero.Base.Shape, Is.EqualTo(EBaseShapeKind.Circle));
        Assert.That(hero.Base.DiameterInches, Is.EqualTo(40f / 25.4f).Within(0.001f));
        Assert.That(hero.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_Core("Hero")));
        Assert.That(hero.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_CoreNumeric("Tough", 6)));

        WeaponFileEntry rifle = hero.Weapons.Single(w => w.Name == "Champion Rifle");
        Assert.That(rifle.RangeInches, Is.EqualTo(24));
        Assert.That(rifle.Attacks, Is.EqualTo(2));
        Assert.That(rifle.ArmorPenetration, Is.EqualTo(1)); // AP folded out of specialRules
        Assert.That(rifle.SpecialRules, Is.Empty);
    }

    [Test]
    public void Import_MergesCombinedPair_AndRepointsNothingItShouldnt()
    {
        ArmyListFile army = Import().Army;

        UnitFileEntry grunts = army.Units.Single(u => u.Name.StartsWith("Grunts"));
        Assert.That(grunts.Name, Is.EqualTo("Grunts (Combined)"));
        Assert.That(grunts.ModelCount, Is.EqualTo(10));
        Assert.That(grunts.PointCost, Is.EqualTo(320));
        Assert.That(grunts.Id, Is.EqualTo("SelHostA")); // the host half survives under its own handle
        Assert.That(grunts.JoinsUnitId, Is.Null);       // combined linkage is consumed, not a hero join

        // Identical profiles consolidated across the halves.
        Assert.That(grunts.Weapons.Single(w => w.Name == "Rifle").Quantity, Is.EqualTo(10));
        Assert.That(grunts.Weapons.Single(w => w.Name == "CCW").Quantity, Is.EqualTo(10));
        Assert.That(grunts.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_Core("Strider")));

        // The hero joined the host half and still points at the merged unit.
        UnitFileEntry hero = army.Units.Single(u => u.Name == "Champion");
        Assert.That(hero.JoinsUnitId, Is.EqualTo("SelHostA"));
    }

    [Test]
    public void Import_FoldsItemRulesIntoUnit_AndToleratesStringRating()
    {
        ArmyListFile army = Import().Army;
        UnitFileEntry support = army.Units.Single(u => u.Name == "Support");

        // Item content rule folded into the unit's rule list; the item itself is not a weapon.
        Assert.That(support.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_Core("Shield Wall")));
        Assert.That(support.Weapons.Select(w => w.Name), Is.EqualTo(new[] { "Launcher" }));

        // Tough(3) survives the string-typed rating.
        Assert.That(support.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_CoreNumeric("Tough", 3)));

        // Rectangle base from "60x35".
        Assert.That(support.Base.Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
    }

    [Test]
    public void Import_MergesSelectedUpgradeRuleGains_Deduped()
    {
        UnitFileEntry support = Import().Army.Units.Single(u => u.Name == "Support");
        Assert.That(support.SpecialRules.Count(r => r == new SpecialRuleEntry_Core("Ambush")), Is.EqualTo(1),
            "the same rule gain selected twice must not duplicate");
    }

    [Test]
    public void Import_SurfacesCampaignAndForceOrgFindings()
    {
        OprListImportResult result = Import();
        Assert.That(result.ListErrors, Is.EqualTo(new[] { "Points limit exceeded: 515/500" }));
        Assert.That(result.Warnings, Has.Some.Contains("campaign XP/traits"),
            "support unit carries xp/traits and must be disclosed");
        Assert.That(result.Warnings, Has.Some.Contains("multiple army books"));
    }

    [Test]
    public void Import_DanglingHeroJoin_WarnsAndDeploysSolo()
    {
        string json = ListJson.Replace("\"joinToUnit\": \"SelHostA\", \"selectionId\": \"SelHero\"",
            "\"joinToUnit\": \"NoSuchUnit\", \"selectionId\": \"SelHero\"");
        OprListImportResult result = OprListImporter.Import(json, Books);

        UnitFileEntry hero = result.Army.Units.Single(u => u.Name == "Champion");
        Assert.That(hero.JoinsUnitId, Is.Null);
        Assert.That(result.Warnings, Has.Some.Contains("joins a unit that is not in the list"));
    }

    [Test]
    public void Import_WrongGameSystem_Throws()
    {
        string json = ListJson.Replace("\"gameSystem\": \"gf\"", "\"gameSystem\": \"aof\"");
        var ex = Assert.Throws<InvalidOperationException>(() => OprListImporter.Import(json, Books));
        Assert.That(ex!.Message, Does.Contain("aof"));
    }

    [Test]
    public void Import_BookOutsideSupportedVersion_ThrowsTyped()
    {
        var newerBooks = new Dictionary<string, string>(Books)
        {
            ["bookA"] = """{"uid":"bookA","name":"Legion Alpha","versionString":"3.6.1","units":[]}""",
        };
        var ex = Assert.Throws<OprVersionMismatchException>(() => OprListImporter.Import(ListJson, newerBooks));
        Assert.That(ex!.BookName, Is.EqualTo("Legion Alpha"));
        Assert.That(ex.FoundVersion, Is.EqualTo("3.6.1"));
    }

    [Test]
    public void Import_MissingBookJson_Throws()
    {
        var onlyA = new Dictionary<string, string> { ["bookA"] = Books["bookA"] };
        var ex = Assert.Throws<InvalidOperationException>(() => OprListImporter.Import(ListJson, onlyA));
        Assert.That(ex!.Message, Does.Contain("bookB"));
    }

    [Test]
    public void VersionMatches_ComparesComponents_NotStringPrefix()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OprListImporter.VersionMatches("3.5.3"), Is.True);
            Assert.That(OprListImporter.VersionMatches("3.5.2"), Is.True);
            Assert.That(OprListImporter.VersionMatches("3.5"), Is.True);
            Assert.That(OprListImporter.VersionMatches("3.6.0"), Is.False);
            Assert.That(OprListImporter.VersionMatches("3.50.1"), Is.False, "component compare, not StartsWith");
            Assert.That(OprListImporter.VersionMatches("4.5.1"), Is.False);
            Assert.That(OprListImporter.VersionMatches(""), Is.False);
        });
    }

    [Test]
    public void AttachBookDefinitions_CopiesDefs_AndUnresolvedRulesReportsHonestly()
    {
        ArmyListFile army = Import().Army;

        // Before attach: core rules (Hero, Tough, Strider, Ambush) resolve; the faction-only names don't.
        IReadOnlyList<string> before = OprListImporter.UnresolvedRuleNames(army);
        Assert.That(before, Is.EqualTo(new[] { "Shield Wall" }));

        var book = new BookFile();
        book.RuleDefinitions.Add(new SpecialRuleDefinition("Shield Wall",
            Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>()));
        OprListImporter.AttachBookDefinitions(army, book);

        Assert.That(army.RuleDefinitions, Has.Count.EqualTo(1));
        Assert.That(army.RuleDefinitions, Is.Not.SameAs(book.RuleDefinitions), "attached lists are copies");
        Assert.That(OprListImporter.UnresolvedRuleNames(army), Is.Empty);
    }
}
