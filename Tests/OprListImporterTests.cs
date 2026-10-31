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
        // #437: OPR 3.6 books; the live corpus mixes patch levels within one major.minor, so the two differ.
        ["bookA"] = """{"uid":"bookA","name":"Legion Alpha","versionString":"3.6.0","units":[]}""",
        ["bookB"] = """{"uid":"bookB","name":"Legion Beta","versionString":"3.6.1","units":[]}""",
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
        Assert.That(grunts.Name, Is.EqualTo("Grunts"), "a merged pair keeps the plain unit name - no (Combined) suffix");
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

    // #378: AoF shares GDF's core ruleset, so an 'aof' list imports and records its system slug.
    // The other OPR systems (skirmish/regiments variants) still refuse.
    [Test]
    public void Import_AgeOfFantasyList_ImportsAndStampsGameSystem()
    {
        string json = ListJson.Replace("\"gameSystem\": \"gf\"", "\"gameSystem\": \"aof\"");
        OprListImportResult result = OprListImporter.Import(json, Books);
        Assert.That(result.Army.GameSystem, Is.EqualTo(GameSystems.AgeOfFantasy));
    }

    [Test]
    public void Import_GrimdarkList_StampsGameSystem()
    {
        Assert.That(Import().Army.GameSystem, Is.EqualTo(GameSystems.GrimdarkFuture));
    }

    [Test]
    public void Import_WrongGameSystem_Throws()
    {
        string json = ListJson.Replace("\"gameSystem\": \"gf\"", "\"gameSystem\": \"gff\"");
        var ex = Assert.Throws<InvalidOperationException>(() => OprListImporter.Import(json, Books));
        Assert.That(ex!.Message, Does.Contain("gff"));
    }

    [Test]
    public void Import_BookOutsideSupportedVersion_ThrowsTyped()
    {
        var newerBooks = new Dictionary<string, string>(Books)
        {
            ["bookA"] = """{"uid":"bookA","name":"Legion Alpha","versionString":"3.7.0","units":[]}""",
        };
        var ex = Assert.Throws<OprVersionMismatchException>(() => OprListImporter.Import(ListJson, newerBooks));
        Assert.That(ex!.BookName, Is.EqualTo("Legion Alpha"));
        Assert.That(ex.FoundVersion, Is.EqualTo("3.7.0"));
        Assert.That(ex.Message, Does.Contain("is at OPR version 3.7.0; this game supports 3.6.x"));
        Assert.That(ex.Message, Does.Contain("game's data needs an update"), "a newer book: our data trails OPR");
    }

    // #437: 3.6 replaces 3.5, so a book still at 3.5 is refused too, with a message naming both versions (ASCII -
    // it is shown in-game) and the remedy for that direction.
    [Test]
    public void Import_A35Book_IsRefused_NamingBothVersions()
    {
        var olderBooks = new Dictionary<string, string>(Books)
        {
            ["bookB"] = """{"uid":"bookB","name":"Legion Beta","versionString":"3.5.3","units":[]}""",
        };
        var ex = Assert.Throws<OprVersionMismatchException>(() => OprListImporter.Import(ListJson, olderBooks));
        Assert.That(ex!.BookName, Is.EqualTo("Legion Beta"));
        Assert.That(ex.Message, Does.StartWith("Army book 'Legion Beta' is at OPR version 3.5.3; this game supports 3.6.x."));
        Assert.That(ex.Message, Does.Contain("has not been updated"));
        Assert.That(ex.Message.All(c => c <= 127), Is.True, "game text is ASCII-only");
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
            Assert.That(OprListImporter.VersionMatches("3.6.0"), Is.True);
            Assert.That(OprListImporter.VersionMatches("3.6.2"), Is.True);
            Assert.That(OprListImporter.VersionMatches("3.6"), Is.True);
            Assert.That(OprListImporter.VersionMatches("3.5.3"), Is.False, "3.6 replaces 3.5 (#437)");
            Assert.That(OprListImporter.VersionMatches("3.7.0"), Is.False);
            Assert.That(OprListImporter.VersionMatches("3.60.1"), Is.False, "component compare, not StartsWith");
            Assert.That(OprListImporter.VersionMatches("4.6.1"), Is.False);
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

    // #367 — the Blessed Sisters Procession Altar shape, the bug that surfaced this: a Transport(6) hull
    // buys Extended Cargo (an item granting Transport(6)) and Army Forge shows Transport(12). The rating
    // arrives THREE times in one share list - `rules` (base), `loadout` (final gear), and the
    // selectedUpgrade's own gains - and only the loadout copy is a second real grant.
    private const string StackListJson = """
    {
      "id": "STACKLIST", "name": "Stacking", "gameSystem": "gf", "pointsLimit": 500,
      "campaignMode": false, "narrativeMode": false, "listPoints": 250,
      "units": [
        { "id": "altar", "cost": 230, "name": "Altar", "size": 1, "bases": { "round": "50" },
          "rules": [ {"name":"Fearless"}, {"name":"Tough","rating":6}, {"name":"Transport","rating":6} ],
          "quality": 4, "defense": 2, "armyId": "bookA", "combined": false,
          "joinToUnit": null, "selectionId": "SelAltar",
          "selectedUpgrades": [
            { "option": { "cost": 20, "gains": [ { "type": "ArmyBookItem", "name": "Extended Cargo",
                "content": [ {"type":"ArmyBookRule","name":"Transport","rating":"6"} ] } ] } }
          ],
          "loadout": [
            { "type": "ArmyBookWeapon", "name": "Flame Blasts", "count": 1, "range": 12, "attacks": 2, "specialRules": [] },
            { "type": "ArmyBookItem", "name": "Aura", "count": 1,
              "content": [ {"type":"ArmyBookRule","name":"Fearless"} ] },
            { "type": "ArmyBookItem", "name": "Extended Cargo", "count": 1,
              "content": [ {"type":"ArmyBookRule","name":"Transport","rating":"6"} ] }
          ] }
      ],
      "forceOrgErrors": []
    }
    """;

    [Test]
    public void Import_ItemGrantedRating_StacksOntoTheBaseRule_AndIsCountedOnce()
    {
        ArmyListFile army = OprListImporter.Import(StackListJson, Books).Army;
        UnitFileEntry altar = army.Units.Single(u => u.Name == "Altar");

        // 6 (base rule) + 6 (the loadout item) - the selectedUpgrade echo of the SAME item must not add a third.
        Assert.That(altar.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_CoreNumeric("Transport", 12)));
        Assert.That(altar.SpecialRules.Single(r => r is SpecialRuleEntry_CoreNumeric { Name: "Transport" })
            .PrintableName, Is.EqualTo("Transport(12)"), "the derived name tracks the stacked rating");

        // An un-rated duplicate is still one rule: the Aura item's Fearless does not double the base one.
        Assert.That(altar.SpecialRules.Count(r => r is SpecialRuleEntry_Core { Name: "Fearless" }), Is.EqualTo(1));

        // Untouched rated rules keep their single rating.
        Assert.That(altar.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_CoreNumeric("Tough", 6)));
    }

    // The same list without a `loadout` - the fallback shape, where the gear above is only the BASE kit, so
    // the selectedUpgrade's item IS the grant and has to be read there instead. Same answer either way.
    [Test]
    public void Import_WithoutLoadout_StacksTheGainedItemInstead()
    {
        string noLoadout = StackListJson
            .Replace("\"loadout\": [", "\"items\": [ {\"type\":\"ArmyBookItem\",\"name\":\"Aura\",\"count\":1," +
                "\"content\":[{\"type\":\"ArmyBookRule\",\"name\":\"Fearless\"}]} ], \"unusedLoadout\": [");

        ArmyListFile army = OprListImporter.Import(noLoadout, Books).Army;
        UnitFileEntry altar = army.Units.Single(u => u.Name == "Altar");

        Assert.That(altar.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_CoreNumeric("Transport", 12)));
        Assert.That(altar.SpecialRules.Count(r => r is SpecialRuleEntry_Core { Name: "Fearless" }), Is.EqualTo(1));
    }

    // #437 - a 3.6.0 share-list unit, trimmed from a live /api/tts response (High Elf Fleets list UGDhJMMH0QBP,
    // fetched 2026-10-30, served resolved against the live 3.6.0 book): an Anti-Gravity Tank whose loadout carries
    // an Assault cannon, the 3.6 Overrun melee weapon (attacks already multiplied: A4 = attacks 4, attacksMultiplier 4)
    // and two selected upgrades whose flat `cost` disagrees with the tank's own costs[] entry (55 vs 80, 65 vs 30).
    private const string ListJson36 = """
    {
      "id": "UGDhJMMH0QBP", "name": "High Elf Fleets", "gameSystem": "gf", "pointsLimit": 3000,
      "campaignMode": false, "narrativeMode": false, "versionChanged": false, "listPoints": 645,
      "units": [
        { "id": "NNVOyAx", "cost": 535, "name": "Anti-Gravity Tank", "size": 1, "quality": 3, "defense": 2,
          "rules": [ {"id":"KdgwlPb8LuD2","name":"Fast","label":"Fast"}, {"id":"a0YtInGiUDd6","name":"Tough","rating":12,"label":"Tough(12)"} ],
          "loadout": [
            { "id": "i6kCRfk3", "name": "Hull Shard Cannon", "type": "ArmyBookWeapon", "count": 1, "range": 12, "attacks": 3,
              "weaponId": "Oglu9AuE", "attacksMultiplier": 1, "originalCount": 1,
              "label": "Hull Shard Cannon (12\", A3, Anti-Cover, Assault, Fracture)",
              "specialRules": [ {"type":"ArmyBookRule","id":"efmjJPTe0Yzd","name":"Anti-Cover","label":"Anti-Cover"},
                                {"type":"ArmyBookRule","id":"FF5ddXgx75lI","name":"Assault","label":"Assault"},
                                {"type":"ArmyBookRule","id":"JSnFcm3SCiMD","name":"Fracture","label":"Fracture"} ] },
            { "id": "mmIw7lbH", "name": "Overrun", "type": "ArmyBookWeapon", "count": 1, "range": 0, "attacks": 4,
              "weaponId": "9yR1y88z", "attacksMultiplier": 4, "originalCount": 1, "label": "Overrun (A4, Thrust)",
              "specialRules": [ {"type":"ArmyBookRule","id":"umbiz4tnEyF8","name":"Thrust","label":"Thrust"} ] },
            { "id": null, "name": "Heavy Focal Cannon", "type": "ArmyBookWeapon", "count": 1, "range": 36, "attacks": 2,
              "weaponId": "dfhsdKVE", "attacksMultiplier": 2, "label": "Heavy Focal Cannon (36\", A2, AP(4), Deadly(6))",
              "specialRules": [ {"id":"17crjK7P6_w6","name":"AP","type":"ArmyBookRule","rating":4},
                                {"id":"_mrPya5SXSqW","name":"Deadly","type":"ArmyBookRule","rating":6} ], "dependencies": [] },
            { "id": "xOq9WGUh", "name": "Hologram Field", "type": "ArmyBookItem", "bases": null, "count": 1,
              "label": "Hologram Field (Stealth)",
              "content": [ {"id":"9jyvRoBODD-O","name":"Stealth","type":"ArmyBookRule","dependencies":[],"count":1} ] }
          ],
          "selectedUpgrades": [
            { "instanceId": "KI7jHYz_P",
              "upgrade": { "id": "CinFtiX", "uid": "-1b_g", "label": "Replace 2x Shard Cannon", "variant": "replace" },
              "option": { "uid": "s7mp3", "id": "s7mp3", "cost": 55,
                "costs": [ {"cost":80,"unitId":"NNVOyAx","exactCost":78.78,"questCost":235} ],
                "label": "Heavy Focal Cannon (36\", A2, AP(4), Deadly(6))",
                "gains": [ {"id":null,"name":"Heavy Focal Cannon","type":"ArmyBookWeapon","count":1,"range":36,"attacks":2,"attacksMultiplier":2} ] } },
            { "instanceId": "_UaVRgcB9",
              "upgrade": { "id": "TQrtsBr", "uid": "Gz6Ba", "label": "Upgrade with", "variant": "upgrade" },
              "option": { "uid": "U6Ar8", "id": "U6Ar8", "cost": 65,
                "costs": [ {"cost":30,"unitId":"NNVOyAx","exactCost":30.06,"questCost":90},
                           {"cost":10,"unitId":"iAyekvp","exactCost":7.52,"questCost":25} ],
                "label": "Hologram Field (Stealth)",
                "gains": [ {"id":"xOq9WGUh","name":"Hologram Field","type":"ArmyBookItem","bases":null,"count":1,
                            "content":[{"id":"9jyvRoBODD-O","name":"Stealth","type":"ArmyBookRule"}]} ] } }
          ],
          "armyId": "7i7blhft75q9zfdc", "combined": false, "joinToUnit": null, "selectionId": "Hz5L1",
          "bases": { "round": "120x92", "square": "150x100" } }
      ],
      "forceOrgErrors": []
    }
    """;

    private static readonly Dictionary<string, string> Books36 = new()
    {
        ["7i7blhft75q9zfdc"] = """{"uid":"7i7blhft75q9zfdc","name":"High Elf Fleets","versionString":"3.6.0","units":[]}""",
    };

    [Test]
    public void Import_Live36List_PassesTheGate_AndMapsTheWeaponsVerbatim()
    {
        OprListImportResult result = OprListImporter.Import(ListJson36, Books36);
        UnitFileEntry tank = result.Army.Units.Single();

        WeaponFileEntry cannon = tank.Weapons.Single(w => w.Name == "Hull Shard Cannon");
        Assert.That(cannon.RangeInches, Is.EqualTo(12));
        Assert.That(cannon.SpecialRules, Is.EqualTo(new SpecialRuleEntry[]
        {
            new SpecialRuleEntry_Core("Anti-Cover"), new SpecialRuleEntry_Core("Assault"), new SpecialRuleEntry_Core("Fracture"),
        }), "an Assault weapon is ONE entry carrying the rule (#424) - never split into a ranged and a melee copy");

        WeaponFileEntry overrun = tank.Weapons.Single(w => w.Name == "Overrun");
        Assert.That(overrun.RangeInches, Is.Zero, "Overrun is a melee weapon");
        Assert.That(overrun.Attacks, Is.EqualTo(4), "attacks is already multiplied - attacksMultiplier is not applied again");
        Assert.That(overrun.SpecialRules, Is.EqualTo(new SpecialRuleEntry[] { new SpecialRuleEntry_Core("Thrust") }));

        Assert.That(tank.Weapons.Single(w => w.Name == "Heavy Focal Cannon").Attacks, Is.EqualTo(2));
        Assert.That(result.Warnings, Has.None.Contains("attacksMultiplier"), "no longer a fidelity warning");
    }

    [Test]
    public void Import_Live36List_PricesUpgradesFromTheUnitsCostsEntry_NotTheFlatScalar()
    {
        OprListImportResult result = OprListImporter.Import(ListJson36, Books36);

        // 535 base + 80 (Heavy Focal Cannon, costs[] for this tank; flat 55) + 30 (Hologram Field; flat 65) = 645,
        // exactly Army Forge's listPoints - nothing parked, no drift warning.
        Assert.That(result.Army.Units.Single().PointCost, Is.EqualTo(645));
        Assert.That(result.Army.UnattributedPoints, Is.Zero);
        Assert.That(result.Warnings, Has.None.Contains("pts"));
    }
}
