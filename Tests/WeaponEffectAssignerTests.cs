using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #239 — the authoring-time effect-set policy: keyword matching for cross-faction tech, per-faction
// defaults, (faction, name) overrides, and the two bake paths (ListCompiler at forge time, ApplyToArmy
// for the one-shot retrofit of existing files). Explicit keys in data always win and nulls are omitted
// on save so untouched files stay byte-identical.
[TestFixture]
public class WeaponEffectAssignerTests
{
    // ---------------- keyword matching ----------------

    [TestCase("Twin Heavy Plasma Rifle", true, "plasma-bolt")]
    [TestCase("Fusion-Mod", true, "fusion-melta")]
    [TestCase("Magic Bolt", true, "arcane-psychic")] // arcane outranks storm-tracer's "bolt"
    [TestCase("Storm Rifle", true, "storm-tracer")]
    [TestCase("Heavy Machinegun", true, null)]       // generic gun -> faction default territory
    [TestCase("Heavy Chainsaw Sword", false, "chain-blade")] // chain outranks blade-standard's "sword"
    [TestCase("Energy Fist", false, "energy-blade")]
    [TestCase("CCW", false, null)]                   // OPR's generic melee placeholder
    public void Match_RoutesByKeywordPriority(string name, bool isRanged, string? expected)
    {
        Assert.That(WeaponEffectAssigner.Match("Nobody", name, isRanged), Is.EqualTo(expected));
    }

    [Test]
    public void Match_FactionNameOverride_BeatsKeywords()
    {
        // "power" normally reads daemonic; the Saurian Power Claw is a lizard's gauntlet. #437: the override
        // is keyed by the 3.6 name and still reached from the 3.5 one (OprBookNames).
        Assert.That(WeaponEffectAssigner.Match("Saurian Starhosts", "Power Claw", isRanged: false),
            Is.EqualTo("claw-rend"));
        Assert.That(WeaponEffectAssigner.Match("Saurian Starhost", "Power Claw", isRanged: false),
            Is.EqualTo("claw-rend"), "the 3.5 book name (bundled until #438, old army files) keeps the override");
        Assert.That(WeaponEffectAssigner.Match("Wormhole Daemons of War", "Power Claw", isRanged: false),
            Is.EqualTo("daemon-arcane-melee"));
    }

    [Test]
    public void FactionDefaults_KnownAndUnknown()
    {
        Assert.That(WeaponEffectAssigner.FactionDefaults("Orc Marauders"),
            Is.EqualTo(("ballistic-slug", "crude-melee")));
        Assert.That(WeaponEffectAssigner.FactionDefaults("People Who Fight"),
            Is.EqualTo(((string?)null, (string?)null)));
    }

    // ---------------- Age of Fantasy (#378) ----------------

    // The colliding faction names are why the game system is part of the key: AoF's Change Disciples
    // are arcane cultists, not the GDF faction of the same name with tracer fire.
    [Test]
    public void FactionDefaults_CollidingDisciplesNames_SplitByGameSystem()
    {
        Assert.That(WeaponEffectAssigner.FactionDefaults("Change Disciples"),
            Is.EqualTo(("ballistic-slug", "energy-blade")), "GDF entry unchanged");
        Assert.That(WeaponEffectAssigner.FactionDefaults("Change Disciples", GameSystems.AgeOfFantasy),
            Is.EqualTo(("arcane-bolt", "blade-standard")));
        Assert.That(WeaponEffectAssigner.FactionDefaults("Wood Elves"),
            Is.EqualTo(((string?)null, (string?)null)), "an AoF faction is unknown to the GDF table");
    }

    // #435 / #437: OPR 3.6 renamed the daemon books in both systems ("Rift Daemons of X" in AoF, "Wormhole Daemons
    // of X" in GDF -> "Daemons of X") and the GDF "Saurian Starhost" -> "Saurian Starhosts". The tables are keyed by
    // the 3.6 names; the 3.5 names reach the same row through OprBookNames, and each system keeps its OWN row for the
    // now-shared name "Daemons of X".
    [TestCase("Change")]
    [TestCase("Lust")]
    [TestCase("Plague")]
    [TestCase("War")]
    public void FactionDefaults_The36DaemonsBookNames_KeepTheirSets_FromEitherName(string god)
    {
        var aof = WeaponEffectAssigner.FactionDefaults($"Daemons of {god}", GameSystems.AgeOfFantasy);
        var gdf = WeaponEffectAssigner.FactionDefaults($"Daemons of {god}");
        Assert.That(aof.Ranged, Is.Not.Null);
        Assert.That(gdf.Ranged, Is.Not.Null);
        Assert.That(WeaponEffectAssigner.FactionDefaults($"Rift Daemons of {god}", GameSystems.AgeOfFantasy), Is.EqualTo(aof));
        Assert.That(WeaponEffectAssigner.FactionDefaults($"Wormhole Daemons of {god}"), Is.EqualTo(gdf));
        Assert.That(WeaponEffectAssigner.FactionDefaults($"Wormhole Daemons of {god}", GameSystems.AgeOfFantasy),
            Is.EqualTo(((string?)null, (string?)null)), "a GDF-only 3.5 name means nothing in the AoF table");
    }

    [Test]
    public void FactionDefaults_SaurianStarhosts_FromEitherName()
    {
        Assert.That(WeaponEffectAssigner.FactionDefaults("Saurian Starhosts"), Is.EqualTo(("gauss-particle", "claw-rend")));
        Assert.That(WeaponEffectAssigner.FactionDefaults("Saurian Starhost"), Is.EqualTo(("gauss-particle", "claw-rend")));
    }

    [TestCase("Repeater Crossbow", true, "crossbow-bolt")] // crossbow outranks "bow"
    [TestCase("Light Bolt Thrower", true, "ballista-bolt")] // NOT storm-tracer's "bolt"
    [TestCase("Longbow", true, "arrow-loose")]
    [TestCase("Giant Sling", true, "sling-stone")]
    [TestCase("Firepowder Rifle", true, "ballistic-slug")] // fantasy gunpowder IS ballistic
    [TestCase("Flame Pistol", true, "breath-flame")]       // payload outranks the gun casing
    [TestCase("Magic Staff", true, "arcane-bolt")]
    [TestCase("Hand Weapon", false, null)]                 // the AoF generic -> faction default
    [TestCase("Great Weapon", false, "great-weapon-smash")]
    [TestCase("Deadly Fangs", false, "beast-maw")]
    [TestCase("Chain-Sword", false, "crude-melee")]        // a chained sword, not a GDF chainsaw
    public void Match_AofVocabulary(string name, bool isRanged, string? expected)
    {
        Assert.That(WeaponEffectAssigner.Match("Nobody", name, isRanged, GameSystems.AgeOfFantasy),
            Is.EqualTo(expected));
        Assert.That(WeaponEffectAssigner.Match("Nobody", "Light Bolt Thrower", isRanged: true),
            Is.EqualTo("storm-tracer"), "the GDF vocabulary is untouched");
    }

    [Test]
    public void ApplyToBook_AofBook_StampsAofDefaults()
    {
        var book = new BookFile { Faction = "Ghostly Undead", GameSystem = GameSystems.AgeOfFantasy };
        Assert.That(WeaponEffectAssigner.ApplyToBook(book), Is.True);
        Assert.That((book.DefaultRangedEffectSet, book.DefaultMeleeEffectSet),
            Is.EqualTo(("arcane-bolt", "spectral-touch")));

        var systemless = new BookFile { Faction = "Ghostly Undead" };
        Assert.That(WeaponEffectAssigner.ApplyToBook(systemless), Is.False,
            "no system field means GDF, whose table does not know AoF factions");
    }

    // ---------------- #379: melee form upgrades + keyword gap fixes + bombing runs ----------------

    [TestCase("Giant Hammer", "great-weapon-smash")]    // size word + blunt noun -> overhead smash
    [TestCase("Great Bone Mace", "great-weapon-smash")] // the pair may be separated by other words
    [TestCase("Titan Mace", "great-weapon-smash")]
    [TestCase("Flail", "crude-melee")]                  // a bare blunt noun stays crude
    [TestCase("Great Plague Hammer", "toxic-melee")]    // payload rows keep their priority
    [TestCase("Toxin Claws", "toxic-rend")]             // toxic + claw keeps the rake motion
    [TestCase("Toxic Maw", "toxic-rend")]
    [TestCase("Censer Flail", "toxic-melee")]           // toxic without a claw stays a slash
    [TestCase("Hooves", "titan-impact")]                // a mount's kick is a blunt impact
    [TestCase("Heavy Hooves", "titan-impact")]
    [TestCase("Bash", "crude-melee")]
    public void Match_AofMeleeFormUpgrades(string name, string expected)
    {
        Assert.That(WeaponEffectAssigner.Match("Nobody", name, isRanged: false, GameSystems.AgeOfFantasy),
            Is.EqualTo(expected));
    }

    [TestCase("Heavy Hammer", "great-weapon-smash")]  // the GDF two-handers get the same treatment
    [TestCase("Heavy Energy Hammer", "energy-blade")] // payload still wins in the GDF vocabulary too
    [TestCase("Titan Hammer", "titan-impact")]        // GDF's own titan row outranks the upgrade
    [TestCase("Toxin Claws", "toxic-rend")]
    public void Match_GdfMeleeFormUpgrades(string name, string expected)
    {
        Assert.That(WeaponEffectAssigner.Match("Nobody", name, isRanged: false), Is.EqualTo(expected));
    }

    [TestCase("Death Stare", "arcane-bolt")]          // a magical gaze is not a crossbow bolt
    [TestCase("Mind-Piercer Screech", "arcane-bolt")]
    [TestCase("Blowpipe", "bio-organic")]             // a poison dart is not a javelin
    public void Match_AofRangedKeywordAdditions(string name, string expected)
    {
        Assert.That(WeaponEffectAssigner.Match("Nobody", name, isRanged: true, GameSystems.AgeOfFantasy),
            Is.EqualTo(expected));
    }

    [Test]
    public void Match_RangeZeroStrafing_IsABombingRun()
    {
        var bombs = new WeaponFileEntry
        {
            Name = "Flame Bombs", RangeInches = 0, Attacks = 2,
            SpecialRules = { new SpecialRuleEntry_Core("Strafing") },
        };
        Assert.That(WeaponEffectAssigner.Match("Nobody", bombs, GameSystems.AgeOfFantasy),
            Is.EqualTo("bombing-run"), "the rule is the signature - Flame Bombs is not a flame sword");
        Assert.That(WeaponEffectAssigner.Match("Nobody", bombs), Is.EqualTo("bombing-run"),
            "GDF bomb racks strafe too");

        var aliased = new WeaponFileEntry
        {
            Name = "Sky-Charges", RangeInches = 0, Attacks = 2,
            SpecialRules = { new SpecialRuleEntry_Alias("Sky Attack", new SpecialRuleEntry_Core("Strafing")) },
        };
        Assert.That(WeaponEffectAssigner.Match("Nobody", aliased, GameSystems.AgeOfFantasy),
            Is.EqualTo("bombing-run"), "an aliased Strafing still reads as a bombing run");

        var ranged = new WeaponFileEntry
        {
            Name = "Stone Thrower", RangeInches = 18, Attacks = 1,
            SpecialRules = { new SpecialRuleEntry_Core("Strafing") },
        };
        Assert.That(WeaponEffectAssigner.Match("Nobody", ranged, GameSystems.AgeOfFantasy),
            Is.EqualTo("sling-stone"), "a genuinely ranged strafing weapon keeps its ranged voice");
    }

    // ---------------- #438: OPR 3.6 weapon names ----------------

    [TestCase("Burst Cyclic Rifle", true, "storm-tracer")]       // 3.6's Storm Rifle
    [TestCase("Anti-Matter Rifle", true, "gravity-pulse")]       // 3.6's Gravity Rifle
    [TestCase("Dark-Matter Pistol", true, "gravity-pulse")]      // 3.6's Anti-Gravity Pistol
    [TestCase("Arc-Charger Pistol", true, "gauss-particle")]
    [TestCase("Neural Psy-Blast", true, "arcane-psychic")]
    [TestCase("Burst Chem-Pistol", true, "bio-organic")]
    [TestCase("Backpack HE-Launcher", true, "mortar-artillery")] // the trailing generic-launcher row...
    [TestCase("Acid Launcher", true, "bio-organic")]             // ...loses to a payload row above it
    [TestCase("Disposable Mini-GL", true, "mortar-artillery")]
    [TestCase("Phosphor Sprayers", true, "flame-jet")]
    [TestCase("Phosphor Spitters", true, "bio-organic")]         // "spit" outranks the trailing phosphor row
    [TestCase("Radium Cannon", true, "gauss-particle")]
    [TestCase("Radium Pulse Gun", true, "laser-beam")]           // "pulse" outranks the trailing radium row
    [TestCase("Overrun", false, "titan-impact")]                 // the 3.6 vehicle/monster ram
    [TestCase("Magma Sword", false, "energy-blade")]             // 3.6's Plasma Sword
    [TestCase("Psy-Witch Staff", false, "daemon-arcane-melee")]
    public void Match_Gdf36Names(string name, bool isRanged, string expected)
    {
        Assert.That(WeaponEffectAssigner.Match("Nobody", name, isRanged), Is.EqualTo(expected));
    }

    [TestCase("Fire Blast", true, "arcane-bolt")]
    [TestCase("Serpent-God Strike", true, "arcane-bolt")]
    [TestCase("Haunting Howl", true, "arcane-bolt")]
    [TestCase("Soul Cannon", true, "ballistic-slug")]            // the trailing caster row loses to "cannon"
    [TestCase("Throw Giant Boulders", true, "sling-stone")]
    [TestCase("Toss Logs", true, "sling-stone")]
    [TestCase("Barbed Net", true, "thrown-spear")]
    [TestCase("Overrun", false, "titan-impact")]
    [TestCase("Sweeping Titan Strike", false, "titan-impact")]
    [TestCase("Jagged Teeth", false, "beast-maw")]
    [TestCase("Reaping Sickle", false, "spear-pierce")]
    [TestCase("Sorcerer Staff", false, "energy-blade")]
    [TestCase("Sword & Staff", false, "blade-standard")]         // the trailing staff row loses to "sword"
    public void Match_Aof36Names(string name, bool isRanged, string expected)
    {
        Assert.That(WeaponEffectAssigner.Match("Nobody", name, isRanged, GameSystems.AgeOfFantasy),
            Is.EqualTo(expected));
    }

    [Test]
    public void Match_RangeZeroCrossingStrike_IsABombingRun()
    {
        // #431 renamed Strafing "Crossing Strike"; 3.6 bomb racks carry the new name (Strafing stays for 3.5 files).
        var bombs = new WeaponFileEntry
        {
            Name = "Flame Bombs", RangeInches = 0, Attacks = 2,
            SpecialRules = { new SpecialRuleEntry_Core("Crossing Strike") },
        };
        Assert.That(WeaponEffectAssigner.Match("Nobody", bombs), Is.EqualTo("bombing-run"));
        Assert.That(WeaponEffectAssigner.Match("Nobody", bombs, GameSystems.AgeOfFantasy), Is.EqualTo("bombing-run"));
    }

    [Test]
    public void FactionDefaults_VoidspaceStalkers_HasARow()
    {
        Assert.That(WeaponEffectAssigner.FactionDefaults("Voidspace Stalkers"), Is.EqualTo(("ballistic-slug", "claw-rend")));
    }

    // ---------------- forge-time bake (ListCompiler) ----------------

    [Test]
    public void Compile_BakesKeywordKeys_AndCopiesBookDefaults()
    {
        BookFile book = EffectBook(rangedDefault: "laser-beam", meleeDefault: "shock-melee");
        BuiltArmyFile army = ListCompiler.Compile(book, OneSquad());

        Assert.That(army.DefaultRangedEffectSet, Is.EqualTo("laser-beam"), "explicit book default wins");
        Assert.That(army.DefaultMeleeEffectSet, Is.EqualTo("shock-melee"));

        UnitFileEntry squad = army.Units.Single();
        Assert.That(Wpn(squad, "Plasma Rifle").EffectSet, Is.EqualTo("plasma-bolt"), "keyword bake");
        Assert.That(Wpn(squad, "CCW").EffectSet, Is.Null, "no keyword -> army default covers it at load");
        Assert.That(Wpn(squad, "Ancient Relic Gun").EffectSet, Is.EqualTo("shard-crystal"),
            "an explicit book key survives the compile clone");
    }

    [Test]
    public void Compile_BookWithoutDefaults_FallsBackToFactionTable()
    {
        BuiltArmyFile army = ListCompiler.Compile(EffectBook(), OneSquad());

        Assert.That(army.DefaultRangedEffectSet, Is.EqualTo("gauss-particle"),
            "a pre-#239 book snapshot still yields its faction's defaults");
        Assert.That(army.DefaultMeleeEffectSet, Is.EqualTo("titan-impact"));
    }

    // ---------------- one-shot retrofit (ApplyToArmy) ----------------

    [Test]
    public void ApplyToArmy_FillsDefaultsAndBakesKeywords_Idempotently()
    {
        ArmyListFile army = OrkArmy();

        Assert.That(WeaponEffectAssigner.ApplyToArmy(army), Is.True, "first pass patches");

        Assert.That(army.DefaultRangedEffectSet, Is.EqualTo("ballistic-slug"));
        Assert.That(army.DefaultMeleeEffectSet, Is.EqualTo("crude-melee"));
        UnitFileEntry boyz = army.Units.Single();
        Assert.That(Wpn(boyz, "Heavy Machinegun").EffectSet, Is.Null, "generic name stays on the default");
        Assert.That(Wpn(boyz, "Missile Launcher").EffectSet, Is.EqualTo("missile-rocket"));
        Assert.That(Wpn(boyz, "Weird Gun").EffectSet, Is.EqualTo("flame-jet"),
            "an explicit key already in the file is never touched");

        Assert.That(WeaponEffectAssigner.ApplyToArmy(army), Is.False, "second pass is a no-op");
    }

    [Test]
    public void KeylessData_SerializesWithoutEffectFields()
    {
        ArmyListFile army = OrkArmy();
        army.Faction = "People Who Fight"; // unknown faction: no defaults get filled
        army.Units.Single().Weapons.RemoveAll(w => w.EffectSet != null);

        string before = JsonSerializer.Serialize(army, RuleJson.Options);
        Assert.That(before.ToLowerInvariant(), Does.Not.Contain("effectset"),
            "null keys/defaults are omitted so pre-#239 files round-trip unchanged");

        WeaponEffectAssigner.ApplyToArmy(army); // bakes Missile Launcher only
        string after = JsonSerializer.Serialize(army, RuleJson.Options);
        Assert.That(after, Does.Contain("\"effectSet\": \"missile-rocket\""));
    }

    // ---------------- fixtures ----------------

    private static WeaponFileEntry Wpn(UnitFileEntry u, string name) => u.Weapons.Single(w => w.Name == name);

    private static BookFile EffectBook(string? rangedDefault = null, string? meleeDefault = null) => new()
    {
        Name = "Effect Test Book",
        Faction = "Robot Legions",
        DefaultRangedEffectSet = rangedDefault,
        DefaultMeleeEffectSet = meleeDefault,
        Units =
        {
            new RosterUnit
            {
                Id = "squad", Name = "Squad", Quality = 4, Defense = 4,
                BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 10,
                Weapons =
                {
                    new WeaponFileEntry { Name = "Plasma Rifle", Quantity = 1, RangeInches = 24, Attacks = 1 },
                    new WeaponFileEntry { Name = "CCW", Quantity = 1, RangeInches = 0, Attacks = 1 },
                    new WeaponFileEntry
                    {
                        Name = "Ancient Relic Gun", Quantity = 1, RangeInches = 18, Attacks = 1,
                        EffectSet = "shard-crystal",
                    },
                },
            },
        },
    };

    private static BuilderList OneSquad() => new()
    {
        Name = "L", BookName = "Effect Test Book", PointsLimit = 1000,
        Units = { new BuilderUnit { RosterUnitId = "squad", ModelCount = 1 } },
    };

    private static ArmyListFile OrkArmy() => new()
    {
        Name = "Orks", Faction = "Orc Marauders",
        Units =
        {
            new UnitFileEntry
            {
                Name = "Boyz", ModelCount = 2, Quality = 5, Defense = 5,
                Weapons =
                {
                    new WeaponFileEntry { Name = "Heavy Machinegun", Quantity = 1, RangeInches = 30, Attacks = 3 },
                    new WeaponFileEntry { Name = "Missile Launcher", Quantity = 1, RangeInches = 24, Attacks = 1 },
                    new WeaponFileEntry
                    {
                        Name = "Weird Gun", Quantity = 1, RangeInches = 12, Attacks = 1,
                        EffectSet = "flame-jet",
                    },
                },
            },
        },
    };
}
