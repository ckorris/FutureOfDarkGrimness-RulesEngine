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
    public void Import_MapsSection_AndFlattensItemGainIntoRule()
    {
        UpgradeSection section = Import().Units.Single().Sections.Single();
        Assert.That(section.Variant, Is.EqualTo(UpgradeVariant.Replace));
        Assert.That(section.Affects, Is.EqualTo(UpgradeAffects.Any));
        Assert.That(section.Targets, Is.EqualTo(new[] { "Blaster" }));

        UpgradeOption option = section.Options.Single();
        Assert.That(option.Cost, Is.EqualTo(10));
        Assert.That(option.WeaponsGained.Single().Name, Is.EqualTo("Heavy Blaster"));
        Assert.That(option.WeaponsGained.Single().ArmorPenetration, Is.EqualTo(2));
        // The "Targeter" item was flattened into the rule it bundles.
        Assert.That(option.RulesGained, Has.One.EqualTo(new SpecialRuleEntry_Core("Reliable")));
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
