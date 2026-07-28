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
    public void Import_AsciiFoldsBookText()
    {
        // Game text is ASCII-only (CLAUDE.md): the font atlas has no glyphs beyond Latin-1, so OPR's
        // typographic characters must fold at import. Corpus-verbatim shapes: the em-dash, curly quotes,
        // and the i-macron (Alien Hives). The curly DOUBLE quotes are the treacherous case — they fold to
        // '"', so the fold must run on parsed string values, never on the raw JSON text (folding raw text
        // would inject an unescaped quote and corrupt the document — 29/47 corpus books hit exactly that).
        // Truly unfoldable characters warn instead of mangling.
        string json = """{"name":"Kī’s “Legion” — Elite","versionString":"1.0","units":[]}""";
        var warnings = new List<string>();

        BookFile book = OprBookImporter.Import(json, "src", "lic", warnings.Add);

        Assert.That(book.Name, Is.EqualTo("Ki's \"Legion\" - Elite"));
        Assert.That(warnings, Is.Empty);

        Assert.That(OprBookImporter.AsciiFold("世"), Is.EqualTo("世"),
            "an unfoldable character is preserved, not mangled");
        var leftoverWarnings = new List<string>();
        OprBookImporter.AsciiFold("世", leftoverWarnings.Add);
        Assert.That(leftoverWarnings, Has.Count.EqualTo(1));
    }

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

    // #197 P17: a NON-numeric rating is a TEXT argument (Spawn's "Spores [5]" names the unit it places),
    // kept as a coreText entry - flattening it to the bare name is how Spawn shipped dead.
    [Test]
    public void Import_KeepsATextRating_AsATextEntry()
    {
        const string json = """
        {
          "name": "Spawners", "versionString": "9.9",
          "units": [
            { "id": "u1", "name": "Beast", "size": 1, "cost": 100, "quality": 4, "defense": 4,
              "weapons": [], "rules": [ {"name":"Spawn","rating":"Spores [5]"} ], "items": [], "upgrades": [] }
          ]
        }
        """;

        BookFile book = OprBookImporter.Import(json, "src", "lic");

        Assert.That(book.Units.Single().Rules.Single(),
            Is.EqualTo(new SpecialRuleEntry_Text("Spawn", "Spores [5]")));
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

    // #219: when OPR omits the flat `cost` scalar, the real price lives in a per-unit `costs` array keyed by
    // unit id - the SAME shared option costs 10 pts on one hero and 5 on another. The importer must read this
    // unit's entry, not treat the option as free. A genuinely uncosted option (no cost, no costs) stays flagged.
    // #261 extends this with the case #219 never covered: both keys present and DISAGREEING ("holo"), which
    // is the common shape (3314 of the corpus's 8434 pairs) - and the one the original flat-first order got
    // wrong. "generic" pins the fallback when a unit has no per-unit entry at all.
    private const string PerUnitCostJson = """
    {
      "name": "Cost Legion", "versionString": "3.5.3",
      "units": [
        { "id": "noble",   "name": "Noble",   "size": 1, "cost": 45, "quality": 3, "defense": 4, "upgrades": ["P1"] },
        { "id": "protector","name": "Protector","size": 1, "cost": 35, "quality": 4, "defense": 5, "upgrades": ["P1"] }
      ],
      "upgradePackages": [
        { "uid": "P1", "sections": [
          { "id":"s1", "label":"Wargear",
            "options":[
              { "id":"pistol", "label":"Master Laser Pistol",
                "costs":[ {"cost":10,"unitId":"noble"}, {"cost":5,"unitId":"protector"} ] },
              { "id":"holo", "label":"Hologram Field", "cost":65,
                "costs":[ {"cost":30,"unitId":"noble"}, {"cost":15,"unitId":"protector"} ] },
              { "id":"onlynoble", "label":"Noble's Own", "costs":[ {"cost":20,"unitId":"noble"} ] },
              { "id":"generic", "label":"Generic Only", "cost":40 },
              { "id":"free", "label":"Trinket", "cost":0 },
              { "id":"mystery", "label":"Unknowable" }
            ] }
        ] }
      ]
    }
    """;

    [Test]
    public void Import_ResolvesPerUnitOptionCost_FromCostsArray()
    {
        BookFile book = OprBookImporter.Import(PerUnitCostJson, "src", "lic");
        UpgradeOption Opt(string unitName, string id) =>
            book.Units.Single(u => u.Name == unitName).Sections.Single().Options.Single(o => o.Id == id);

        // Same option, different price per unit - and neither is "unpriced".
        Assert.That(Opt("Noble", "pistol").Cost, Is.EqualTo(10));
        Assert.That(Opt("Noble", "pistol").CostUnpriced, Is.False);
        Assert.That(Opt("Protector", "pistol").Cost, Is.EqualTo(5));
        Assert.That(Opt("Protector", "pistol").CostUnpriced, Is.False);

        // Explicit 0 stays free; an option with neither cost nor costs stays flagged unpriced.
        Assert.That(Opt("Noble", "free").CostUnpriced, Is.False);
        Assert.That(Opt("Noble", "mystery").CostUnpriced, Is.True);
        Assert.That(Opt("Noble", "mystery").Cost, Is.EqualTo(0));
    }

    // #197 (Darkborn): OPR uses the bare name "Darkborn" for two different rules across armies. The catalog
    // registers them disambiguated ("Darkborn (Offensive)"/"(Defensive)"), so the bare name resolved to
    // nothing; the importer routes it to the right variant by army name. A rule name carried on a unit, a
    // weapon, and an upgrade option all disambiguate (the walk reaches every rule-reference site).
    private const string DarkbornJson = """
    {
      "name": "%ARMY%", "versionString": "1.0",
      "units": [
        { "id": "u1", "name": "Reaver", "size": 1, "cost": 50, "quality": 3, "defense": 3,
          "weapons": [ {"name":"Blade","count":1,"range":null,"attacks":2,"specialRules":[{"name":"Darkborn"}]} ],
          "rules": [ {"name":"Darkborn"} ],
          "upgrades": ["P1"] }
      ],
      "upgradePackages": [
        { "uid": "P1", "sections": [
          { "id":"s1", "label":"Blessing", "variant":"upgrade", "affects":{"type":"all"},
            "options":[ { "id":"o1", "label":"Dark Gift", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Darkborn"}] } ] }
        ] }
      ]
    }
    """;

    [TestCase("Dark Brothers", "Darkborn (Defensive)")]
    [TestCase("Dark Prime Brothers", "Darkborn (Offensive)")]
    public void Import_DisambiguatesDarkborn_ByArmy(string army, string expected)
    {
        BookFile book = OprBookImporter.Import(DarkbornJson.Replace("%ARMY%", army), "src", "lic");
        RosterUnit reaver = book.Units.Single();

        Assert.That(reaver.Rules, Has.One.EqualTo(new SpecialRuleEntry_Core(expected)),
            "unit-level Darkborn should route to the army's variant");
        Assert.That(reaver.Weapons.Single().SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_Core(expected)),
            "weapon-level Darkborn should route too");
        Assert.That(reaver.Sections.Single().Options.Single().RulesGained,
            Has.One.EqualTo(new SpecialRuleEntry_Core(expected)),
            "upgrade-gained Darkborn should route too");
        Assert.That(book.Units.SelectMany(u => u.Rules), Has.None.EqualTo(new SpecialRuleEntry_Core("Darkborn")),
            "the bare name must not survive for an army that defines Darkborn");
    }

    [Test]
    public void Import_LeavesDarkbornUntouched_ForAnUnlistedArmy()
    {
        // Only the two armies that actually define "Darkborn" are disambiguated; a stray reference in any
        // other book is left as-is (it simply stays an unresolved reference, exactly as before).
        BookFile book = OprBookImporter.Import(DarkbornJson.Replace("%ARMY%", "Some Other Legion"), "src", "lic");
        Assert.That(book.Units.Single().Rules, Has.One.EqualTo(new SpecialRuleEntry_Core("Darkborn")));
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
        { "id":"f", "name":"F", "size":1, "cost":10, "quality":4, "defense":4, "bases":{"round":"60x35","square":""} },
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

        // round "120x92" — an oval → our rectangle footprint. #225: OPR writes LENGTH first, and the
        // engine runs HeightInches along the facing, so 120 (length) is the Height and 92 the Width.
        Assert.That(Base("b").Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(Base("b").HeightInches, Is.EqualTo(120f / MmPerInch).Within(0.001f));
        Assert.That(Base("b").WidthInches, Is.EqualTo(92f / MmPerInch).Within(0.001f));

        // round "none" → fall back to the square "175x125".
        Assert.That(Base("c").Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(Base("c").HeightInches, Is.EqualTo(175f / MmPerInch).Within(0.001f));
        Assert.That(Base("c").WidthInches, Is.EqualTo(125f / MmPerInch).Within(0.001f));

        // A 60x35 bike base is the unambiguous case: a bike is 60mm long and 35mm wide, so it must face
        // along its 60mm axis. Height > Width here is the invariant every real rectangular base satisfies.
        Assert.That(Base("f").Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(Base("f").HeightInches, Is.EqualTo(60f / MmPerInch).Within(0.001f));
        Assert.That(Base("f").WidthInches, Is.EqualTo(35f / MmPerInch).Within(0.001f));
        Assert.That(Base("f").HeightInches, Is.GreaterThan(Base("f").WidthInches),
            "a rectangular base must be longer along its facing axis than it is wide");

        // Both empty, or no bases field at all → #225 defect B ESTIMATES a base from the unit's rules
        // rather than silently falling through to the 28mm circle, which used to put tanks and titans on
        // an infantry dot. These fixtures carry no rules, so they estimate as the smallest vehicle.
        // (DefaultBaseEstimatorTests covers the estimate itself; this pins that the importer routes to it.)
        foreach (string id in new[] { "d", "e" })
        {
            Assert.That(DefaultBaseEstimator.IsUnsizedDefault(Base(id)), Is.False,
                "an undeclared base must be estimated, never left as the 28mm placeholder");
            Assert.That(Base(id).Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
            Assert.That(Base(id).HeightInches, Is.EqualTo(90f / MmPerInch).Within(0.001f));
            Assert.That(Base(id).WidthInches, Is.EqualTo(52f / MmPerInch).Within(0.001f));
        }
    }

    [Test]
    public void Import_WarnsForEveryEstimatedBase_SoGuessesAreNeverSilent()
    {
        var warnings = new List<string>();
        OprBookImporter.Import(BasesJson, "TestSource", "CC-BY-SA 4.0", warnings.Add);

        // Units "d" and "e" declare no base; "a"/"b"/"c" all resolve from real specs.
        Assert.That(warnings.FindAll(w => w.Contains("no base declared")), Has.Count.EqualTo(2));
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
          "effect": "Pick one enemy unit within 18\", which counts as being in Difficult Terrain once (next time the effect would apply)." },
        { "name": "Cursed Stride", "threshold": 1,
          "effect": "Pick one enemy unit within 18\", which counts as being in Dangerous Terrain once (next time the effect would apply)." }
      ]
    }
    """;

    [Test]
    public void Import_ParsesMovementApLossAndMoraleTestSpells_SynthesizingRules()
    {
        var warnings = new List<string>();
        BookFile book = OprBookImporter.Import(ExtendedSpellsJson, "TestSource", "CC-BY-SA 4.0", warnings.Add);

        Assert.That(book.Spells, Has.Count.EqualTo(7), "every corpus spell shape now parses");
        Assert.That(warnings, Is.Empty);

        // Counts-as-terrain (#153 primitive): both kinds synthesize a CountAsInTerrain rule.
        SpellDefinition pestilence = book.Spells.Single(s => s.Name == "Aura of Pestilence");
        Assert.That(((Effect.AddRule)pestilence.Effect).RuleName, Is.EqualTo("Aura of Pestilence Effect"));
        SpecialRuleDefinition pestilenceRule = book.RuleDefinitions.Single(d => d.Name == "Aura of Pestilence Effect");
        Assert.That(((Effect.CountAsInTerrain)pestilenceRule.Passive[0].Effect).Terrain,
            Is.EqualTo(ECountAsTerrain.Difficult));
        SpecialRuleDefinition strideRule = book.RuleDefinitions.Single(d => d.Name == "Cursed Stride Effect");
        Assert.That(((Effect.CountAsInTerrain)strideRule.Passive[0].Effect).Terrain,
            Is.EqualTo(ECountAsTerrain.Dangerous));

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

        // Only the synthesized rules land in the book; parse-only spells embed nothing.
        Assert.That(book.RuleDefinitions.Select(d => d.Name),
            Is.EquivalentTo(new[]
            {
                "Shock Speed Effect", "Deceleration Rune Effect", "Corrode Weapons Effect",
                "Aura of Pestilence Effect", "Cursed Stride Effect",
            }));
    }

    // OPR "select" is the pick bound per section ("Upgrade with one/any/up to two:"), distinct from
    // "affects" (models touched per application). Unconsumed before this test existed, every section
    // defaulted to one pick — mis-bounding the 118 "any" + 4 "up to two" sections in the corpus.
    private const string SelectJson = """
    {
      "name": "Select", "versionString": "1",
      "units": [ { "id": "u1", "name": "Squad", "size": 5, "cost": 100, "quality": 4, "defense": 4,
                   "weapons": [], "rules": [], "upgrades": ["P1"] } ],
      "upgradePackages": [
        { "uid": "P1", "sections": [
          { "id":"any", "label":"Upgrade with any", "variant":"upgrade", "select":{"type":"any"},
            "options":[
              { "id":"a1", "label":"A", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Fast"}] },
              { "id":"a2", "label":"B", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Furious"}] },
              { "id":"a3", "label":"C", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Stealth"}] }
            ] },
          { "id":"upto", "label":"Upgrade with up to two", "variant":"upgrade", "select":{"type":"up to","value":2},
            "options":[
              { "id":"b1", "label":"D", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Scout"}] },
              { "id":"b2", "label":"E", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Strider"}] },
              { "id":"b3", "label":"F", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Agile"}] }
            ] },
          { "id":"one", "label":"Upgrade with one", "variant":"upgrade", "select":{"type":"exactly","value":1},
            "options":[ { "id":"c1", "label":"G", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Hero"}] } ] },
          { "id":"none", "label":"No select key", "variant":"upgrade",
            "options":[ { "id":"d1", "label":"H", "cost":5, "gains":[{"type":"ArmyBookRule","name":"Slow"}] } ] }
        ] }
      ]
    }
    """;

    [Test]
    public void Import_MapsSelectToMaxPicks()
    {
        BookFile book = OprBookImporter.Import(SelectJson, "TestSource", "CC-BY-SA 4.0");
        RosterUnit squad = book.Units.Single();

        Assert.That(squad.Sections.Single(s => s.Id == "any").MaxPicks, Is.EqualTo(3),
            "'any' → every option may be picked");
        Assert.That(squad.Sections.Single(s => s.Id == "upto").MaxPicks, Is.EqualTo(2));
        Assert.That(squad.Sections.Single(s => s.Id == "one").MaxPicks, Is.EqualTo(1));
        Assert.That(squad.Sections.Single(s => s.Id == "none").MaxPicks, Is.EqualTo(1),
            "absent select keeps the one-pick default");
    }

    // #219 (2026-07-19): OPR writes an explicit "cost": 0 for a genuinely free option, and OMITS the key
    // entirely on options it prices inside its own points algorithm rather than publishing a number. A
    // non-nullable int collapsed both onto 0, so unpriced upgrades imported as free and every list carrying
    // one silently came in light. Verified against the real High Elf Fleets book, which mixes both shapes.
    private const string CostShapeJson = """
    {
      "name": "Costs", "versionString": "1",
      "units": [ { "id": "u1", "name": "Squad", "size": 5, "cost": 100, "quality": 4, "defense": 4,
                   "weapons": [], "rules": [], "upgrades": ["P1"] } ],
      "upgradePackages": [
        { "uid": "P1", "sections": [
          { "id":"s1", "label":"Upgrade with", "variant":"upgrade",
            "options":[
              { "id":"priced",   "label":"Priced",   "cost":15, "gains":[{"type":"ArmyBookRule","name":"Fast"}] },
              { "id":"free",     "label":"Free",     "cost":0,  "gains":[{"type":"ArmyBookRule","name":"Scout"}] },
              { "id":"unpriced", "label":"Unpriced",            "gains":[{"type":"ArmyBookRule","name":"Stealth"}] }
            ] }
        ] }
      ]
    }
    """;

    [Test]
    public void Import_DistinguishesUnpricedOptionFromGenuinelyFreeOption()
    {
        BookFile book = OprBookImporter.Import(CostShapeJson, "TestSource", "CC-BY-SA 4.0");
        List<UpgradeOption> options = book.Units.Single().Sections.Single().Options;

        UpgradeOption priced = options.Single(o => o.Id == "priced");
        Assert.That(priced.Cost, Is.EqualTo(15));
        Assert.That(priced.CostUnpriced, Is.False);

        UpgradeOption free = options.Single(o => o.Id == "free");
        Assert.That(free.Cost, Is.EqualTo(0));
        Assert.That(free.CostUnpriced, Is.False, "an explicit 0 means free, and must stay distinguishable");

        UpgradeOption unpriced = options.Single(o => o.Id == "unpriced");
        Assert.That(unpriced.Cost, Is.EqualTo(0), "we have no number to charge - but see the flag");
        Assert.That(unpriced.CostUnpriced, Is.True, "an ABSENT cost key is 'OPR never published a price'");
    }

    // #261: OPR publishes BOTH a flat generic `cost` and a per-unit `costs[]` array on most options, and
    // they usually disagree - the flat number is a leftover generic price, and Army Forge charges the
    // per-unit one. #219 read flat-first, which mispriced 3314 of the 8434 (unit, option) pairs across the
    // 47 bundled books (e.g. High Elf Fleets' Anti-Gravity Tank billed 65 for a 30-point Hologram Field).
    [Test]
    public void Import_PerUnitCost_BeatsTheFlatGenericCost()
    {
        BookFile book = OprBookImporter.Import(PerUnitCostJson, "src", "lic");
        UpgradeOption Opt(string unitName, string id) =>
            book.Units.Single(u => u.Name == unitName).Sections.Single().Options.Single(o => o.Id == id);

        Assert.That(Opt("Noble", "holo").Cost, Is.EqualTo(30), "the Noble's own price, not the flat 65");
        Assert.That(Opt("Protector", "holo").Cost, Is.EqualTo(15), "same option, cheaper on the Protector");
        Assert.That(Opt("Noble", "holo").CostUnpriced, Is.False);
    }

    [Test]
    public void Import_FallsBackToTheFlatCost_WhenThisUnitHasNoPerUnitEntry()
    {
        BookFile book = OprBookImporter.Import(PerUnitCostJson, "src", "lic");
        UpgradeOption Opt(string unitName, string id) =>
            book.Units.Single(u => u.Name == unitName).Sections.Single().Options.Single(o => o.Id == id);

        // No costs[] at all - the flat number is all there is, on both units.
        Assert.That(Opt("Noble", "generic").Cost, Is.EqualTo(40));
        Assert.That(Opt("Protector", "generic").Cost, Is.EqualTo(40));

        // costs[] lists only the Noble, so the Protector falls through - and with no flat cost either it is
        // genuinely unpriced rather than silently free.
        Assert.That(Opt("Noble", "onlynoble").Cost, Is.EqualTo(20));
        Assert.That(Opt("Protector", "onlynoble").CostUnpriced, Is.True);
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
