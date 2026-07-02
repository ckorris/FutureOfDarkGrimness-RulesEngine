using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
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

    // The real corpus shapes verbatim (High Elf Fleets / Alien Hives v3.5.3), plus one unparseable.
    private const string SpellsJson = """
    {
      "name": "Spells", "versionString": "1", "units": [], "upgradePackages": [],
      "spells": [
        { "name": "Psychic Blast", "threshold": 2,
          "effect": "Pick one enemy unit within 12\", which takes 4 hits with AP(2)." },
        { "name": "Elemental Seeker", "threshold": 1,
          "effect": "Pick one enemy unit within 18\", which takes 1 hit with Blast(3)." },
        { "name": "Psy-Destruction", "threshold": 2,
          "effect": "Pick one enemy model within 24\", which takes 2 hits with AP(4). This effect is resolved as if the target was a unit of [1]." },
        { "name": "Hidden Spirits", "threshold": 2,
          "effect": "Pick up to two friendly units within 12\", which get Unpredictable Shooter once (next time the effect would apply)." },
        { "name": "Terror Seeker", "threshold": 2,
          "effect": "Pick up to three enemy units within 18\", which friendly units gets Unpredictable Fighter against once (next time the effect would apply)." },
        { "name": "Weirdness", "threshold": 1,
          "effect": "Something entirely unlike the corpus shapes." }
      ]
    }
    """;

    [Test]
    public void Import_ParsesSpells_DamageBuffAndSingleModel_SkippingUnparseable()
    {
        var warnings = new List<string>();
        BookFile book = OprBookImporter.Import(SpellsJson, "TestSource", "CC-BY-SA 4.0", warnings.Add);

        Assert.That(book.Spells, Has.Count.EqualTo(5));
        Assert.That(warnings, Has.One.Contains("Weirdness"));

        // "friendly units get X against" → the mark family: three enemy targets, attackers gain the rule.
        SpellDefinition terror = book.Spells.Single(s => s.Name == "Terror Seeker");
        Assert.That(terror.Target.MaxCount, Is.EqualTo(3));
        Assert.That(terror.Target.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(((Effect.MarkTarget)terror.Effect).RuleName, Is.EqualTo("Unpredictable Fighter"));

        // Damage: AP folds into ArmorPenetration; targeting is one enemy unit needing LoS.
        SpellDefinition blast = book.Spells.Single(s => s.Name == "Psychic Blast");
        Assert.That(blast.Threshold, Is.EqualTo(2));
        Assert.That(blast.Target.RangeInches, Is.EqualTo(12f));
        Assert.That(blast.Target.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(blast.Target.RequireLineOfSight, Is.True);
        var blastHits = (Effect.DealHits)blast.Effect;
        Assert.That(blastHits.Count, Is.EqualTo(4));
        Assert.That(blastHits.ArmorPenetration, Is.EqualTo(2));
        Assert.That(blastHits.WithRules, Is.Empty);

        // Non-AP weapon rules ride WithRules for load-time resolution.
        var seekerHits = (Effect.DealHits)book.Spells.Single(s => s.Name == "Elemental Seeker").Effect;
        Assert.That(seekerHits.WithRules, Is.EqualTo(new[] { "Blast(3)" }));

        // "model" target → SingleModel confinement (the 'unit of [1]' wording).
        SpellDefinition psy = book.Spells.Single(s => s.Name == "Psy-Destruction");
        Assert.That(psy.Target.SingleModel, Is.True);
        Assert.That(((Effect.DealHits)psy.Effect).ArmorPenetration, Is.EqualTo(4));

        // Buff: up-to-two friendly units, granted "once" → NextTrigger, no LoS requirement.
        SpellDefinition spirits = book.Spells.Single(s => s.Name == "Hidden Spirits");
        Assert.That(spirits.Target.MaxCount, Is.EqualTo(2));
        Assert.That(spirits.Target.TargetAffinity, Is.EqualTo(ETargetAffinity.Friend));
        var grant = (Effect.AddRule)spirits.Effect;
        Assert.That(grant.RuleName, Is.EqualTo("Unpredictable Shooter"));
        Assert.That(grant.Scope, Is.EqualTo(ELifetime.NextTrigger));
    }

    // The five remaining corpus shapes verbatim (Dwarf Guilds / HDF / Machine Cults / War Disciples /
    // Soul-Snatcher Cults / Plague Disciples v3.5.3): movement-modifier + AP-loss (synthesized rules),
    // morale-test-then (fatigue / move), and the still-unparseable counts-as-terrain family.
    private const string ExtendedSpellsJson = """
    {
      "name": "Spells2", "versionString": "1", "units": [], "upgradePackages": [],
      "spells": [
        { "name": "Shock Speed", "threshold": 1,
          "effect": "Pick up to three friendly units within 12\", which moves +2\" when using Advance actions and +4\" when using Rush/Charge actions once (next time the effect would apply)." },
        { "name": "Deceleration Rune ", "threshold": 2,
          "effect": "Pick up to three enemy units within 18\", which moves -2\" when using Advance actions and -4\" when using Rush/Charge actions once (next time the effect would apply)." },
        { "name": "Corrode Weapons", "threshold": 2,
          "effect": "Pick up to three enemy units within 18\", which loses AP(1) when shooting once (next time the effect would apply)." },
        { "name": "Terrifying Fury", "threshold": 2,
          "effect": "Pick one enemy unit within 18\", which must take a morale test. If failed, it becomes fatigued." },
        { "name": "Deep Hypnosis", "threshold": 3,
          "effect": "Pick up to two enemy units within 18\", which must take a morale test. If failed you may move it by up to 6\" in a straight line in any direction." },
        { "name": "Aura of Pestilence", "threshold": 1,
          "effect": "Pick one enemy unit within 18\", which counts as being in Difficult Terrain once (next time the effect would apply)." }
      ]
    }
    """;

    [Test]
    public void Import_ParsesMovementApLossAndMoraleTestSpells_SynthesizingRules()
    {
        var warnings = new List<string>();
        BookFile book = OprBookImporter.Import(ExtendedSpellsJson, "TestSource", "CC-BY-SA 4.0", warnings.Add);

        Assert.That(book.Spells, Has.Count.EqualTo(5));
        Assert.That(warnings, Has.One.Contains("Aura of Pestilence"), "counts-as-terrain still skips honestly");

        // Movement grant: spell grants a synthesized "<Spell> Effect" rule once; the rule carries the
        // three signed MovementBonus entries and is embedded in the book.
        SpellDefinition shock = book.Spells.Single(s => s.Name == "Shock Speed");
        Assert.That(((Effect.AddRule)shock.Effect).RuleName, Is.EqualTo("Shock Speed Effect"));
        Assert.That(((Effect.AddRule)shock.Effect).Scope, Is.EqualTo(ELifetime.NextTrigger));
        SpecialRuleDefinition shockRule = book.RuleDefinitions.Single(d => d.Name == "Shock Speed Effect");
        Assert.That(shockRule.Passive, Has.Count.EqualTo(3));
        Assert.That(shockRule.Valence, Is.EqualTo(EValence.Positive));
        var advance = (Effect.MovementBonus)shockRule.Passive[0].Effect;
        Assert.That((advance.ActionType, advance.DistanceInches), Is.EqualTo((EActionType.Advance, 2f)));
        var charge = (Effect.MovementBonus)shockRule.Passive[2].Effect;
        Assert.That((charge.ActionType, charge.DistanceInches), Is.EqualTo((EActionType.Charge, 4f)));

        // Negative variant: trailing-space name trimmed, negative bonuses, Negative valence, enemy target.
        SpellDefinition rune = book.Spells.Single(s => s.Name == "Deceleration Rune");
        Assert.That(rune.Target.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(rune.Target.MaxCount, Is.EqualTo(3));
        SpecialRuleDefinition runeRule = book.RuleDefinitions.Single(d => d.Name == "Deceleration Rune Effect");
        Assert.That(runeRule.Valence, Is.EqualTo(EValence.Negative));
        Assert.That(((Effect.MovementBonus)runeRule.Passive[1].Effect).DistanceInches, Is.EqualTo(-4f));

        // AP loss: synthesized attacker-side ReduceArmorPenetration, shooting-only.
        SpellDefinition corrode = book.Spells.Single(s => s.Name == "Corrode Weapons");
        Assert.That(((Effect.AddRule)corrode.Effect).RuleName, Is.EqualTo("Corrode Weapons Effect"));
        SpecialRuleDefinition corrodeRule = book.RuleDefinitions.Single(d => d.Name == "Corrode Weapons Effect");
        Assert.That(((Effect.ReduceArmorPenetration)corrodeRule.Passive[0].Effect).Amount, Is.EqualTo(1));
        Assert.That(corrodeRule.Passive[0].Condition, Is.InstanceOf<Condition.Not>());

        // Morale-test-then: the #034 conditional family, both corpus consequences.
        SpellDefinition fury = book.Spells.Single(s => s.Name == "Terrifying Fury");
        Assert.That(((Effect.MoraleTestThen)fury.Effect).OnFailure, Is.InstanceOf<Effect.ApplyFatigue>());
        SpellDefinition hypnosis = book.Spells.Single(s => s.Name == "Deep Hypnosis");
        var hypnosisMove = (Effect.TriggeredMove)((Effect.MoraleTestThen)hypnosis.Effect).OnFailure;
        Assert.That((hypnosisMove.MaxInches, hypnosisMove.IsOptional), Is.EqualTo((6f, true)));
        Assert.That(hypnosis.Target.MaxCount, Is.EqualTo(2));

        // Only the three synthesized rules land in the book; parse-only spells embed nothing.
        Assert.That(book.RuleDefinitions.Select(d => d.Name),
            Is.EquivalentTo(new[] { "Shock Speed Effect", "Deceleration Rune Effect", "Corrode Weapons Effect" }));
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
