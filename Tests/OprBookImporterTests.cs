using System.Linq;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #153 (P0b) — the OPR Army Forge JSON → BookFile mapping, on a small synthetic book (our own, not OPR data):
// stat/cost mapping, AP folded into ArmorPenetration, a string-typed rating tolerated, replace/any section
// semantics, and an item gain flattened into its bundled rule. Then the imported book compiles as expected.
[TestFixture]
public class OprBookImporterTests
{
    // Minimal OPR-shaped JSON. "Tough":"3" is a string on purpose — OPR types ratings loosely and the importer
    // must not throw on it.
    private const string OprJson = """
    {
      "name": "Test Legion", "versionString": "9.9",
      "units": [
        { "id": "u1", "name": "Grunts", "size": 5, "cost": 100, "quality": 4, "defense": 4,
          "weapons": [
            { "name": "Blaster", "count": 5, "range": 24, "attacks": 1, "specialRules": [{"name":"AP","rating":1}] },
            { "name": "Claw", "count": 5, "range": null, "attacks": 2, "specialRules": [] }
          ],
          "rules": [ {"name":"Tough","rating":"3"} ],
          "items": [ {"name":"Combat Shields","count":5,"content":[{"type":"ArmyBookRule","name":"Shield Wall"}]} ],
          "upgrades": ["P1"] }
      ],
      "upgradePackages": [
        { "uid": "P1", "sections": [
          { "id":"s1", "label":"Replace any Blaster", "variant":"replace", "affects":{"type":"any"}, "targets":["Blaster"],
            "options":[
              { "id":"o1", "label":"Heavy Blaster", "cost":10, "gains":[
                {"type":"ArmyBookWeapon","name":"Heavy Blaster","count":1,"range":30,"attacks":1,"specialRules":[{"name":"AP","rating":2}]},
                {"type":"ArmyBookItem","name":"Targeter","content":[{"type":"ArmyBookRule","name":"Reliable"}]}
              ] }
            ] }
        ] }
      ]
    }
    """;

    private static BookFile Import() => OprBookImporter.Import(OprJson, "TestSource", "CC-BY-SA 4.0");

    [Test]
    public void Import_MapsUnitStats_AndStampsAttribution()
    {
        BookFile book = Import();
        Assert.That(book.Name, Is.EqualTo("Test Legion"));
        Assert.That(book.Version, Is.EqualTo("OPR v9.9"));
        Assert.That(book.Source, Is.EqualTo("TestSource"));
        Assert.That(book.License, Is.EqualTo("CC-BY-SA 4.0"));

        RosterUnit grunts = book.Units.Single();
        Assert.That(grunts.BaseModelCount, Is.EqualTo(5));
        Assert.That(grunts.Quality, Is.EqualTo(4));
        Assert.That(grunts.Defense, Is.EqualTo(4));
        Assert.That(grunts.BasePointCost, Is.EqualTo(100));
    }

    [Test]
    public void Import_FoldsAP_KeepsOtherRules_AndToleratesStringRating()
    {
        RosterUnit grunts = Import().Units.Single();

        WeaponFileEntry blaster = grunts.Weapons.Single(w => w.Name == "Blaster");
        Assert.That(blaster.ArmorPenetration, Is.EqualTo(1)); // AP folded out of specialRules
        Assert.That(blaster.SpecialRules, Is.Empty);
        Assert.That(blaster.Quantity, Is.EqualTo(5));

        WeaponFileEntry claw = grunts.Weapons.Single(w => w.Name == "Claw");
        Assert.That(claw.RangeInches, Is.EqualTo(0)); // melee (null range → 0)

        // Tough(3) survives despite the rating arriving as the string "3".
        Assert.That(grunts.Rules, Has.One.EqualTo(new SpecialRuleEntry_CoreNumeric("Tough", 3)));
    }

    [Test]
    public void Import_MapsUnitItems_WithNameAndRules()
    {
        ItemEntry shields = Import().Units.Single().Items.Single();
        Assert.That(shields.Name, Is.EqualTo("Combat Shields"));
        Assert.That(shields.Quantity, Is.EqualTo(5));
        Assert.That(shields.Rules, Has.One.EqualTo(new SpecialRuleEntry_Core("Shield Wall")));
    }

    [Test]
    public void Import_MapsSection_AndKeepsItemGainNamed()
    {
        UpgradeSection section = Import().Units.Single().Sections.Single();
        Assert.That(section.Variant, Is.EqualTo(UpgradeVariant.Replace));
        Assert.That(section.Affects, Is.EqualTo(UpgradeAffects.Any));
        Assert.That(section.Targets, Is.EqualTo(new[] { "Blaster" }));

        UpgradeOption option = section.Options.Single();
        Assert.That(option.Cost, Is.EqualTo(10));
        Assert.That(option.WeaponsGained.Single().Name, Is.EqualTo("Heavy Blaster"));
        Assert.That(option.WeaponsGained.Single().ArmorPenetration, Is.EqualTo(2));
        // The "Targeter" item keeps its name (for display + Replace targeting), carrying its rule.
        ItemEntry targeter = option.ItemsGained.Single();
        Assert.That(targeter.Name, Is.EqualTo("Targeter"));
        Assert.That(targeter.Rules, Has.One.EqualTo(new SpecialRuleEntry_Core("Reliable")));
    }

    // Every bases variant observed in the real books: round number, round oval "WxH", round "none" with a
    // square fallback, both empty, and the field absent entirely.
    private const string BasesJson = """
    {
      "name": "Bases", "versionString": "1",
      "units": [
        { "id":"a", "name":"A", "size":1, "cost":10, "quality":4, "defense":4, "bases":{"round":"32","square":"30"} },
        { "id":"b", "name":"B", "size":1, "cost":10, "quality":4, "defense":4, "bases":{"round":"120x92","square":"100x60"} },
        { "id":"c", "name":"C", "size":1, "cost":10, "quality":4, "defense":4, "bases":{"round":"none","square":"175x125"} },
        { "id":"d", "name":"D", "size":1, "cost":10, "quality":4, "defense":4, "bases":{"round":"","square":""} },
        { "id":"e", "name":"E", "size":1, "cost":10, "quality":4, "defense":4 }
      ],
      "upgradePackages": []
    }
    """;

    [Test]
    public void Import_MapsBaseSizes_RoundOvalSquareFallbackAndDefault()
    {
        const float MmPerInch = 25.4f;
        var units = OprBookImporter.Import(BasesJson, "TestSource", "CC-BY-SA 4.0").Units;
        BaseFileEntry Base(string id) => units.Single(u => u.Id == id).Base;

        // round "32" → 32mm circle.
        Assert.That(Base("a").Shape, Is.EqualTo(EBaseShapeKind.Circle));
        Assert.That(Base("a").DiameterInches, Is.EqualTo(32f / MmPerInch).Within(0.001f));

        // round "120x92" — an oval → our rectangle footprint.
        Assert.That(Base("b").Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(Base("b").WidthInches, Is.EqualTo(120f / MmPerInch).Within(0.001f));
        Assert.That(Base("b").HeightInches, Is.EqualTo(92f / MmPerInch).Within(0.001f));

        // round "none" → fall back to the square "175x125".
        Assert.That(Base("c").Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(Base("c").WidthInches, Is.EqualTo(175f / MmPerInch).Within(0.001f));
        Assert.That(Base("c").HeightInches, Is.EqualTo(125f / MmPerInch).Within(0.001f));

        // Both empty, or no bases field at all → the default base (28mm circle).
        foreach (string id in new[] { "d", "e" })
        {
            Assert.That(Base(id).Shape, Is.EqualTo(EBaseShapeKind.Circle));
            Assert.That(Base(id).DiameterInches, Is.EqualTo(BaseShapeDefaults.CircleDiameterInches).Within(0.001f));
        }
    }

    [Test]
    public void ImportedBook_Compiles_WithChosenUpgrade()
    {
        BookFile book = Import();
        var list = new BuilderList
        {
            Units = { new BuilderUnit
            {
                RosterUnitId = "u1", ModelCount = 5,
                Choices = { new UpgradeChoice { SectionId = "s1", OptionId = "o1", Count = 2 } },
            } },
        };

        UnitFileEntry unit = ListCompiler.Compile(book, list).Units.Single();
        Assert.That(unit.PointCost, Is.EqualTo(120));                       // 100 + 10×2
        Assert.That(unit.Weapons.Single(w => w.Name == "Blaster").Quantity, Is.EqualTo(3));  // 5 − 2 replaced
        Assert.That(unit.Weapons.Single(w => w.Name == "Heavy Blaster").Quantity, Is.EqualTo(2));
    }
}
