using System.Linq;
using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.ArmyBuilding;
using NUnit.Framework;

namespace FDG.Tests;

// #153 (P0) — ListCompiler turns a book + selections into the exact playable ArmyListFile the engine consumes.
// Assertions are deterministic against the hand-authored DemoBook: replace-one, add-models (count-scaled),
// replace-all (cost scales with target count), and a rule-granting upgrade.
[TestFixture]
public class ArmyForgeCompilerTests
{
    // Warriors: +1 Plasma (replace one Rifle), +2 Warriors (add-models ×2), +War Banner (Fear).
    // Gunners: replace all 3 Heavy Rifles with Missile Launchers.
    private static BuilderList DemoList() => new()
    {
        Name = "Test List", BookName = "Dark Vanguard", PointsLimit = 500,
        Units =
        {
            new BuilderUnit
            {
                RosterUnitId = "warriors",
                Choices =
                {
                    new UpgradeChoice { SectionId = "warriors-special", OptionId = "plasma", Count = 1 },
                    new UpgradeChoice { SectionId = "warriors-reinforce", OptionId = "add-warrior", Count = 2 },
                    new UpgradeChoice { SectionId = "warriors-banner", OptionId = "war-banner", Count = 1 },
                },
            },
            new BuilderUnit
            {
                RosterUnitId = "gunners",
                Choices = { new UpgradeChoice { SectionId = "gunners-missiles", OptionId = "missile", Count = 1 } },
            },
        },
    };

    private static WeaponFileEntry Wpn(UnitFileEntry u, string name) => u.Weapons.Single(w => w.Name == name);

    [Test]
    public void Compile_Warriors_ReplaceOne_AddModels_And_Upgrade()
    {
        BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), DemoList());
        UnitFileEntry warriors = army.Units.Single(u => u.Name == "Vanguard Warriors");

        // add-warrior ×2 → 5 + 2 models; default weapon count tracks it (6 Rifles + 1 Plasma = 7 = models).
        Assert.That(warriors.ModelCount, Is.EqualTo(7));
        Assert.That(Wpn(warriors, "Rifle").Quantity, Is.EqualTo(6));
        Assert.That(Wpn(warriors, "Plasma Rifle").Quantity, Is.EqualTo(1));
        Assert.That(Wpn(warriors, "Plasma Rifle").SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_CoreNumeric("AP", 2)));

        // War Banner granted Fear.
        Assert.That(warriors.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_Core("Fear")));

        // 65 base + 5 plasma + 13×2 models + 10 banner.
        Assert.That(warriors.PointCost, Is.EqualTo(106));
    }

    [Test]
    public void Compile_Gunners_ReplaceAll_ScalesCostByTargetCount()
    {
        BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), DemoList());
        UnitFileEntry gunners = army.Units.Single(u => u.Name == "Heavy Gunners");

        // All 3 Heavy Rifles swapped for 3 Missile Launchers; cost 120 + 15×3.
        Assert.That(gunners.Weapons.Any(w => w.Name == "Heavy Rifle"), Is.False);
        Assert.That(Wpn(gunners, "Missile Launcher").Quantity, Is.EqualTo(3));
        Assert.That(gunners.ModelCount, Is.EqualTo(3));
        Assert.That(gunners.PointCost, Is.EqualTo(165));
    }

    [Test]
    public void Compile_EmbedsSelectionsAndBook_AndCarriesDefs()
    {
        BookFile book = DemoBook.Build();
        BuilderList list = DemoList();
        BuiltArmyFile army = ListCompiler.Compile(book, list);

        Assert.That(army.Name, Is.EqualTo("Test List"));
        Assert.That(army.Units, Has.Count.EqualTo(2));
        Assert.That(army.Selections, Is.SameAs(list));
        Assert.That(army.Book, Is.SameAs(book));
        // Demo references only core rules by name, so no embedded defs are needed (the RuleValidator gate is
        // trivially satisfied). A book with custom rules would carry them here.
        Assert.That(army.RuleDefinitions, Is.Empty);
    }

    [Test]
    public void Compile_ProducesFileThatDeserializesAsPlainArmy_ForTheEngine()
    {
        BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), DemoList());

        // Exactly what the builder would write (derived type → embed included).
        string json = JsonSerializer.Serialize(army, RuleJson.Options);

        // Exactly what the lobby "Load Army" / headless ArmyLoader read (base type → embed skipped).
        ArmyListFile asArmy = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)!;
        Assert.That(asArmy.Units, Has.Count.EqualTo(2));
        Assert.That(asArmy.Units.Sum(u => u.PointCost), Is.EqualTo(271));
    }

    // ── Replace semantics (target-aware) ────────────────────────────────────────────────────────────────

    private static WeaponFileEntry Wpn(string name, int qty = 1) => new() { Name = name, Quantity = qty, Attacks = 1 };

    private static UnitFileEntry CompileOne(RosterUnit unit, params UpgradeChoice[] choices)
    {
        var book = new BookFile { Name = "T", Units = { unit } };
        var bu = new BuilderUnit { RosterUnitId = unit.Id };
        bu.Choices.AddRange(choices);
        return ListCompiler.Compile(book, new BuilderList { PointsLimit = 100000, Units = { bu } }).Units.Single();
    }

    [Test]
    public void ReplaceOne_WithNoTargetPresent_IsANoOp()
    {
        // Unit has no "Sword" — choosing "replace one Sword" must not add the replacement or charge points.
        var unit = new RosterUnit
        {
            Id = "u", Name = "U", Quality = 4, Defense = 4, BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 10,
            Sections = { new UpgradeSection { Id = "s", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One,
                Targets = { "Sword" }, Options = { new UpgradeOption { Id = "o", Cost = 10, WeaponsGained = { Wpn("Blade") } } } } },
        };
        UnitFileEntry compiled = CompileOne(unit, new UpgradeChoice { SectionId = "s", OptionId = "o", Count = 1 });

        Assert.That(compiled.PointCost, Is.EqualTo(10));                       // no cost added
        Assert.That(compiled.Weapons.Any(w => w.Name == "Blade"), Is.False);  // no replacement added
    }

    [Test]
    public void ReplaceAll_WithPluralTarget_SwapsEveryMatch()
    {
        // "Replace all Energy Swords" (plural) must match the "Energy Sword" weapon and swap all 5.
        var unit = new RosterUnit
        {
            Id = "u", Name = "U", Quality = 4, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
            Weapons = { Wpn("Energy Sword", 5) },
            Sections = { new UpgradeSection { Id = "s", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.All,
                Targets = { "Energy Swords" }, Options = { new UpgradeOption { Id = "o", Cost = 0, WeaponsGained = { Wpn("Shard Carbine") } } } } },
        };
        UnitFileEntry compiled = CompileOne(unit, new UpgradeChoice { SectionId = "s", OptionId = "o", Count = 1 });

        Assert.That(compiled.Weapons.Any(w => w.Name == "Energy Sword"), Is.False);
        Assert.That(compiled.Weapons.Single(w => w.Name == "Shard Carbine").Quantity, Is.EqualTo(5));
    }

    [Test]
    public void Items_FlattenRulesIntoUnit_AndReplaceDeductsThem()
    {
        // Retributors-shaped: 5 Energy Swords (weapons) + 5 Combat Shields (item granting Shield Wall).
        // "Replace all Energy Swords and Combat Shields" must strip BOTH pools (plural targets), add the
        // replacement per match, and drop the item's granted rule from the unit.
        var unit = new RosterUnit
        {
            Id = "u", Name = "U", Quality = 3, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 105,
            Weapons = { Wpn("Energy Sword", 5) },
            Items = { new ItemEntry { Name = "Combat Shields", Quantity = 5, Rules = { new SpecialRuleEntry_Core("Shield Wall") } } },
            Sections = { new UpgradeSection { Id = "s", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.All,
                Targets = { "Energy Swords", "Combat Shields" },
                Options = { new UpgradeOption { Id = "o", Cost = 0, WeaponsGained = { Wpn("Shard Carbine"), Wpn("CCW") } } } } },
        };

        // Without the choice: item rule is on the unit.
        UnitFileEntry plain = CompileOne(unit);
        Assert.That(plain.SpecialRules, Has.One.EqualTo(new SpecialRuleEntry_Core("Shield Wall")));

        // With the choice: swords + shields gone, 5 carbines + 5 CCWs, Shield Wall gone.
        UnitFileEntry swapped = CompileOne(unit, new UpgradeChoice { SectionId = "s", OptionId = "o", Count = 1 });
        Assert.That(swapped.Weapons.Any(w => w.Name == "Energy Sword"), Is.False);
        Assert.That(swapped.Weapons.Single(w => w.Name == "Shard Carbine").Quantity, Is.EqualTo(5));
        Assert.That(swapped.Weapons.Single(w => w.Name == "CCW").Quantity, Is.EqualTo(5));
        Assert.That(swapped.SpecialRules.Contains(new SpecialRuleEntry_Core("Shield Wall")), Is.False);
    }

    [Test]
    public void ReplaceOne_CombinedTarget_RequiresEveryPart()
    {
        // "Replace one Energy Sword and Combat Shield" with shields present but NO swords: min across the
        // combined targets is 0, so the choice is a no-op (no phantom gain, no cost).
        var unit = new RosterUnit
        {
            Id = "u", Name = "U", Quality = 3, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 105,
            Items = { new ItemEntry { Name = "Combat Shields", Quantity = 5 } },
            Sections = { new UpgradeSection { Id = "s", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One,
                Targets = { "Energy Sword", "Combat Shield" },
                Options = { new UpgradeOption { Id = "o", Cost = 5, WeaponsGained = { Wpn("Shard Pistol") } } } } },
        };
        UnitFileEntry compiled = CompileOne(unit, new UpgradeChoice { SectionId = "s", OptionId = "o", Count = 1 });

        Assert.That(compiled.PointCost, Is.EqualTo(105));
        Assert.That(compiled.Weapons.Any(w => w.Name == "Shard Pistol"), Is.False);
    }

    [Test]
    public void Choices_ApplyInSectionOrder_NotClickOrder()
    {
        // "Replace one Shard Carbine" clicked BEFORE "Replace all Energy Swords with Shard Carbines" must
        // still work: compilation orders choices by the book's section order, so the carbine exists by the
        // time the second section is applied.
        var unit = new RosterUnit
        {
            Id = "u", Name = "U", Quality = 3, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 105,
            Weapons = { Wpn("Energy Sword", 5) },
            Sections =
            {
                new UpgradeSection { Id = "all", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.All,
                    Targets = { "Energy Swords" }, Options = { new UpgradeOption { Id = "o1", Cost = 0, WeaponsGained = { Wpn("Shard Carbine") } } } },
                new UpgradeSection { Id = "one", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One,
                    Targets = { "Shard Carbine" }, Options = { new UpgradeOption { Id = "o2", Cost = 20, WeaponsGained = { Wpn("Twin Shard Carbine") } } } },
            },
        };
        UnitFileEntry compiled = CompileOne(unit,
            new UpgradeChoice { SectionId = "one", OptionId = "o2", Count = 1 },   // clicked first
            new UpgradeChoice { SectionId = "all", OptionId = "o1", Count = 1 });  // clicked second

        Assert.That(compiled.Weapons.Single(w => w.Name == "Shard Carbine").Quantity, Is.EqualTo(4));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Twin Shard Carbine").Quantity, Is.EqualTo(1));
        Assert.That(compiled.PointCost, Is.EqualTo(125));
    }

    // ── #107 combined squads (decision 8) ──────────────────────────────────────────────────────────────

    [Test]
    public void CombinedPair_MergesIntoOneUnit_SummingModelsCostAndWeapons()
    {
        BookFile book = DemoBook.Build();
        var list = new BuilderList
        {
            Name = "Combined", PointsLimit = 1000,
            Units =
            {
                new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "a" },
                new BuilderUnit
                {
                    RosterUnitId = "warriors", ModelCount = 5, Id = "b", CombinedWithId = "a",
                    // The second copy buys its own upgrade — GDF "pay for both individually".
                    Choices = { new UpgradeChoice { SectionId = "warriors-special", OptionId = "plasma" } },
                },
                new BuilderUnit { RosterUnitId = "gunners", ModelCount = 3, Id = "c" },
            },
        };

        BuiltArmyFile army = ListCompiler.Compile(book, list);

        Assert.That(army.Units, Has.Count.EqualTo(2), "the pair merged; gunners untouched");
        UnitFileEntry combined = army.Units.Single(u => u.Name.Contains("Combined"));
        Assert.That(combined.ModelCount, Is.EqualTo(10));
        Assert.That(combined.PointCost, Is.EqualTo(65 + 65 + 5), "both copies' base cost plus B's plasma");
        Assert.That(Wpn(combined, "Rifle").Quantity, Is.EqualTo(9), "5 + (5 − 1 replaced)");
        Assert.That(Wpn(combined, "Plasma Rifle").Quantity, Is.EqualTo(1));
        Assert.That(list.Units, Has.Count.EqualTo(3), "the editable list itself is never mutated");
        Assert.That(army.TotalPoints, Is.EqualTo(65 + 65 + 5 + 120));
    }

    [Test]
    public void CombinedLink_DanglingOrCrossRoster_CompilesAsSeparateUnits()
    {
        BookFile book = DemoBook.Build();
        var list = new BuilderList
        {
            Name = "Bad Links", PointsLimit = 1000,
            Units =
            {
                new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "a", CombinedWithId = "gone" },
                new BuilderUnit { RosterUnitId = "gunners", ModelCount = 3, Id = "g" },
                new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "w", CombinedWithId = "g" },
            },
        };

        BuiltArmyFile army = ListCompiler.Compile(book, list);

        Assert.That(army.Units, Has.Count.EqualTo(3), "neither link is a valid same-roster pair");
    }

    [Test]
    public void HeroJoinedToTheAbsorbedCopy_FollowsIntoTheMergedUnit()
    {
        BookFile book = DemoBook.Build();
        var list = new BuilderList
        {
            Name = "Join Follows", PointsLimit = 1000,
            Units =
            {
                new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "a" },
                new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "b", CombinedWithId = "a" },
                new BuilderUnit { RosterUnitId = "gunners", ModelCount = 3, Id = "hero", JoinsUnitId = "b" },
            },
        };

        BuiltArmyFile army = ListCompiler.Compile(book, list);

        UnitFileEntry joiner = army.Units.Single(u => u.Id == "hero");
        Assert.That(joiner.JoinsUnitId, Is.EqualTo("a"), "the join target followed the absorbed copy into the host");
    }

    [Test]
    public void ReplaceAny_IsBoundedByMaxApplicationsAndTargets()
    {
        // "Replace up to 2 Rifles" — asking for 5 is clamped to 2.
        var unit = new RosterUnit
        {
            Id = "u", Name = "U", Quality = 4, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
            Weapons = { Wpn("Rifle", 5) },
            Sections = { new UpgradeSection { Id = "s", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.Any,
                MaxApplications = 2, Targets = { "Rifle" }, Options = { new UpgradeOption { Id = "o", Cost = 3, WeaponsGained = { Wpn("Heavy") } } } } },
        };
        UnitFileEntry compiled = CompileOne(unit, new UpgradeChoice { SectionId = "s", OptionId = "o", Count = 5 });

        Assert.That(compiled.Weapons.Single(w => w.Name == "Rifle").Quantity, Is.EqualTo(3)); // 5 − 2
        Assert.That(compiled.Weapons.Single(w => w.Name == "Heavy").Quantity, Is.EqualTo(2));
        Assert.That(compiled.PointCost, Is.EqualTo(106));                                     // 100 + 3×2
    }
}
