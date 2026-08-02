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
    public void Compile_Gunners_ReplaceAll_AppliesToEveryModel_ButChargesOnce()
    {
        BuiltArmyFile army = ListCompiler.Compile(DemoBook.Build(), DemoList());
        UnitFileEntry gunners = army.Units.Single(u => u.Name == "Heavy Gunners");

        // All 3 Heavy Rifles swapped for 3 Missile Launchers - the EFFECT still reaches every model...
        Assert.That(gunners.Weapons.Any(w => w.Name == "Heavy Rifle"), Is.False);
        Assert.That(Wpn(gunners, "Missile Launcher").Quantity, Is.EqualTo(3));
        Assert.That(gunners.ModelCount, Is.EqualTo(3));

        // ...but a "Replace all" price is flat: 120 + 15 once, NOT 120 + 15x3 (#218, this test previously
        // pinned the overcharge). Verified against a real share list 2026-07-19.
        Assert.That(gunners.PointCost, Is.EqualTo(135));
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
        Assert.That(asArmy.Units.Sum(u => u.PointCost), Is.EqualTo(241)); // 271 before the #218 flat-price fix
    }

    // #218 regression, pinned to the real numbers that settled the convention: the Havoc Brothers share
    // list (iaP7jaKVjbUD) carries a 10-pt "Replace all Heavy Rifles and CCWs" on two 5-model units, and
    // Army Forge's listPoints exceeds the base sum by exactly 20 - not 100. Flat per unit, and NOT scaled
    // by models. Contrast Affects.Any below, which stays per-application.
    [Test]
    public void ReplaceAll_ChargesFlatPerUnit_RegardlessOfModelCount()
    {
        var unit = new RosterUnit
        {
            Id = "havoc", Name = "Havoc Brothers", BaseModelCount = 5, MinModels = 5, MaxModels = 5,
            BasePointCost = 160,
            Weapons = { Wpn("Heavy Rifle", 5) },
            Sections =
            {
                new UpgradeSection
                {
                    Id = "all", Label = "Replace all Heavy Rifles", Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.All, Targets = { "Heavy Rifle" },
                    Options = { new UpgradeOption { Id = "o", Label = "Heavy Pistol",
                        WeaponsGained = { Wpn("Heavy Pistol") }, Cost = 10 } },
                },
            },
        };

        UnitFileEntry compiled = CompileOne(unit, new UpgradeChoice { SectionId = "all", OptionId = "o", Count = 1 });

        Assert.That(compiled.PointCost, Is.EqualTo(170), "160 + 10 flat - NOT 160 + 10x5 models");
        Assert.That(compiled.Weapons.Any(w => w.Name == "Heavy Rifle"), Is.False, "every model still swaps");
        Assert.That(compiled.Weapons.Single(w => w.Name == "Heavy Pistol").Quantity, Is.EqualTo(5));
    }

    [Test]
    public void ReplaceAny_StillChargesPerApplication()
    {
        var unit = new RosterUnit
        {
            Id = "squad", Name = "Squad", BaseModelCount = 3, MinModels = 3, MaxModels = 3,
            BasePointCost = 100,
            Weapons = { Wpn("Axe", 3) },
            Sections =
            {
                new UpgradeSection
                {
                    Id = "any", Label = "Replace any Axe", Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.Any, Targets = { "Axe" }, MaxApplications = 3,
                    Options = { new UpgradeOption { Id = "o", Label = "Sword",
                        WeaponsGained = { Wpn("Sword") }, Cost = 10 } },
                },
            },
        };

        UnitFileEntry compiled = CompileOne(unit, new UpgradeChoice { SectionId = "any", OptionId = "o", Count = 2 });

        Assert.That(compiled.PointCost, Is.EqualTo(120), "100 + 10x2 picks - the All fix must not leak here");
        Assert.That(compiled.Weapons.Single(w => w.Name == "Sword").Quantity, Is.EqualTo(2));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Axe").Quantity, Is.EqualTo(1));
    }

    // ── Replace semantics (target-aware) ────────────────────────────────────────────────────────────────

    private static WeaponFileEntry Wpn(string name, int qty = 1) => new() { Name = name, Quantity = qty, Attacks = 1 };

    // #197 P17: a Spawn/Split TEXT argument names another book unit; the compiler embeds that unit as
    // an auxiliary spec (keyed by the exact argument text in Id, sized by the [n], zero points), and
    // recurses so a Split chain's every link ships (Change Horrors -> Lesser Horrors -> Changelings).
    [Test]
    public void SpawnAndSplitTargets_CompileIntoAuxiliarySpecs_Recursively()
    {
        var horrors = new RosterUnit
        {
            Id = "h", Name = "Change Horrors", BaseModelCount = 5, MinModels = 5, MaxModels = 5,
            BasePointCost = 100,
            Rules = { new SpecialRuleEntry_Text("Split", "Lesser Horrors [5]") },
        };
        var lesser = new RosterUnit
        {
            Id = "l", Name = "Lesser Horrors", BaseModelCount = 10, MinModels = 10, MaxModels = 10,
            BasePointCost = 60,
            Rules = { new SpecialRuleEntry_Text("Split", "Changelings [10]") },
        };
        var changelings = new RosterUnit
        {
            Id = "c", Name = "Changelings", BaseModelCount = 10, MinModels = 10, MaxModels = 10,
            BasePointCost = 30,
        };
        var book = new BookFile { Name = "T", Units = { horrors, lesser, changelings } };
        var list = new BuilderList
        {
            PointsLimit = 100000,
            Units = { new BuilderUnit { RosterUnitId = "h" } },   // only the head of the chain is bought
        };

        var army = ListCompiler.Compile(book, list);

        Assert.That(army.AuxiliaryUnits, Is.Not.Null.And.Count.EqualTo(2),
            "both links of the Split chain ship as auxiliary specs");
        UnitFileEntry lesserSpec = army.AuxiliaryUnits!.Single(u => u.Id == "Lesser Horrors [5]");
        Assert.That(lesserSpec.Name, Is.EqualTo("Lesser Horrors"), "display name stays clean");
        Assert.That(lesserSpec.ModelCount, Is.EqualTo(5), "the [5] overrides the roster's base size");
        Assert.That(lesserSpec.PointCost, Is.EqualTo(0), "placed by a rule, not bought");
        Assert.That(lesserSpec.SpecialRules.OfType<SpecialRuleEntry_Text>().Single().TextValue,
            Is.EqualTo("Changelings [10]"), "the aux unit keeps its own Split argument");
        Assert.That(army.AuxiliaryUnits.Single(u => u.Id == "Changelings [10]").ModelCount, Is.EqualTo(10));
    }

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

    // ── #318 starved Replace: the target arrives from a LATER section ──────────────────────────────────

    // The Titan Lords mini-titans in miniature: one Heavy Hammer + a Titan Shield, with "Replace any Heavy
    // Hammer" authored ABOVE the "Replace Titan Shield" whose only option buys the second hammer.
    private static RosterUnit MiniTitan() => new()
    {
        Id = "titan", Name = "Errant Mini-Titan", Quality = 3, Defense = 2,
        BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 295,
        Weapons = { Wpn("Heavy Hammer"), Wpn("Stomp") },
        Items = { new ItemEntry { Name = "Titan Shield", Quantity = 1, Rules = { new SpecialRuleEntry_Core("Fortified") } } },
        Sections =
        {
            new UpgradeSection
            {
                Id = "hammers", Label = "Replace any Heavy Hammer", Variant = UpgradeVariant.Replace,
                Affects = UpgradeAffects.Any, Targets = { "Heavy Hammer" },
                Options =
                {
                    new UpgradeOption { Id = "sword", Label = "Heavy Sword", Cost = 30, WeaponsGained = { Wpn("Heavy Sword") } },
                    new UpgradeOption { Id = "claw", Label = "Heavy Claw", Cost = 35, WeaponsGained = { Wpn("Heavy Claw") } },
                },
            },
            new UpgradeSection
            {
                Id = "shield", Label = "Replace Titan Shield", Variant = UpgradeVariant.Replace,
                Affects = UpgradeAffects.One, Targets = { "Titan Shield" },
                Options = { new UpgradeOption { Id = "hammer", Label = "Heavy Hammer", Cost = 30, WeaponsGained = { Wpn("Heavy Hammer") } } },
            },
        },
    };

    // #318, the reported bug (friend's War Disciples list, 2026-08-02): with the shield traded for a second
    // Heavy Hammer, BOTH hammers must be swappable. Book order alone starved the second swap - the hammer
    // section applies before the shield section that pays for the hammer - so it was silently clamped to one.
    [Test]
    public void ReplaceAny_TargetGrantedByALaterSection_AppliesTheFullCount()
    {
        UnitFileEntry compiled = CompileOne(MiniTitan(),
            new UpgradeChoice { SectionId = "shield", OptionId = "hammer", Count = 1 },
            new UpgradeChoice { SectionId = "hammers", OptionId = "sword", Count = 2 });

        Assert.That(compiled.Weapons.Any(w => w.Name == "Heavy Hammer"), Is.False, "both hammers were swapped");
        Assert.That(compiled.Weapons.Single(w => w.Name == "Heavy Sword").Quantity, Is.EqualTo(2));
        Assert.That(compiled.PointCost, Is.EqualTo(385), "295 + 30 shield swap + 30x2 hammer swaps");
        Assert.That(compiled.SpecialRules.Contains(new SpecialRuleEntry_Core("Fortified")), Is.False,
            "the shield is gone, so its granted rule goes with it");
    }

    // Two different options in the same starved section: the second hammer feeds whichever pick still owes.
    [Test]
    public void ReplaceAny_TargetGrantedLater_SplitsAcrossOptions()
    {
        UnitFileEntry compiled = CompileOne(MiniTitan(),
            new UpgradeChoice { SectionId = "shield", OptionId = "hammer", Count = 1 },
            new UpgradeChoice { SectionId = "hammers", OptionId = "sword", Count = 1 },
            new UpgradeChoice { SectionId = "hammers", OptionId = "claw", Count = 1 });

        Assert.That(compiled.Weapons.Single(w => w.Name == "Heavy Sword").Quantity, Is.EqualTo(1));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Heavy Claw").Quantity, Is.EqualTo(1));
        Assert.That(compiled.PointCost, Is.EqualTo(390)); // 295 + 30 + 30 + 35
    }

    // The retry must not INVENT applications: without the shield swap there is exactly one hammer, so a
    // count of 2 still buys (and charges for) one - the pre-existing clamp, unchanged.
    [Test]
    public void ReplaceAny_WithoutTheLaterGrant_StillClampsToWhatExists()
    {
        UnitFileEntry compiled = CompileOne(MiniTitan(),
            new UpgradeChoice { SectionId = "hammers", OptionId = "sword", Count = 2 });

        Assert.That(compiled.Weapons.Single(w => w.Name == "Heavy Sword").Quantity, Is.EqualTo(1));
        Assert.That(compiled.PointCost, Is.EqualTo(325), "295 + one 30-pt swap, not two");
    }

    // #318 sibling shape (Battle Brothers pathfinders): the section's target is absent from the BASE loadout
    // entirely and only exists if a later section buys it. The swap used to vanish silently, free of charge.
    [Test]
    public void ReplaceOne_TargetAbsentUntilALaterSectionGrantsIt_StillApplies()
    {
        var unit = new RosterUnit
        {
            Id = "pathfinder", Name = "Elite Pathfinder", Quality = 3, Defense = 3,
            BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 100,
            Weapons = { Wpn("Flamer Pistol") },
            Sections =
            {
                new UpgradeSection
                {
                    Id = "gravity", Label = "Replace Gravity Pistol", Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.One, Targets = { "Gravity Pistol" },
                    Options = { new UpgradeOption { Id = "shotgun", Cost = 5, WeaponsGained = { Wpn("Master Shotgun") } } },
                },
                new UpgradeSection
                {
                    Id = "flamer", Label = "Replace Flamer Pistol", Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.One, Targets = { "Flamer Pistol" },
                    Options = { new UpgradeOption { Id = "grav", Cost = 10, WeaponsGained = { Wpn("Gravity Pistol") } } },
                },
            },
        };

        UnitFileEntry compiled = CompileOne(unit,
            new UpgradeChoice { SectionId = "gravity", OptionId = "shotgun", Count = 1 },
            new UpgradeChoice { SectionId = "flamer", OptionId = "grav", Count = 1 });

        Assert.That(compiled.Weapons.Single().Name, Is.EqualTo("Master Shotgun"),
            "the flamer became a gravity pistol, which the first section then swapped out");
        Assert.That(compiled.PointCost, Is.EqualTo(115), "both swaps are charged");
    }

    // A section that can never be fed (nothing grants its target) still no-ops, and a pair whose targets
    // each want the other's gain settles instead of spinning - the retry runs to a fixpoint, not forever.
    [Test]
    public void StarvedReplace_ThatIsNeverFed_StaysANoOp_AndMutualStarvationTerminates()
    {
        var unit = new RosterUnit
        {
            Id = "u", Name = "U", Quality = 4, Defense = 4, BaseModelCount = 1, MinModels = 1, MaxModels = 1,
            BasePointCost = 50,
            Sections =
            {
                new UpgradeSection { Id = "a", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One,
                    Targets = { "Sword" }, Options = { new UpgradeOption { Id = "o", Cost = 5, WeaponsGained = { Wpn("Axe") } } } },
                new UpgradeSection { Id = "b", Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One,
                    Targets = { "Axe" }, Options = { new UpgradeOption { Id = "o", Cost = 5, WeaponsGained = { Wpn("Sword") } } } },
            },
        };

        UnitFileEntry compiled = CompileOne(unit,
            new UpgradeChoice { SectionId = "a", OptionId = "o", Count = 1 },
            new UpgradeChoice { SectionId = "b", OptionId = "o", Count = 1 });

        Assert.That(compiled.Weapons, Is.Empty, "neither swap ever had a target");
        Assert.That(compiled.PointCost, Is.EqualTo(50), "and neither was charged");
    }

    // A "Replace all" is evaluated once, where the book puts it: it must not come back after a later section
    // grants a fresh copy of its target and eat that too (nor charge its flat price twice).
    [Test]
    public void ReplaceAll_DoesNotReapplyToTargetsGrantedByALaterSection()
    {
        var unit = new RosterUnit
        {
            Id = "u", Name = "U", Quality = 3, Defense = 3, BaseModelCount = 5, MinModels = 5, MaxModels = 5,
            BasePointCost = 100,
            Weapons = { Wpn("Energy Sword", 5), Wpn("CCW") },
            Sections =
            {
                new UpgradeSection { Id = "all", Label = "Replace all Energy Swords", Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.All, Targets = { "Energy Swords" },
                    Options = { new UpgradeOption { Id = "o1", Cost = 10, WeaponsGained = { Wpn("Shard Carbine") } } } },
                new UpgradeSection { Id = "sgt", Label = "Replace CCW", Variant = UpgradeVariant.Replace,
                    Affects = UpgradeAffects.One, Targets = { "CCW" },
                    Options = { new UpgradeOption { Id = "o2", Cost = 20, WeaponsGained = { Wpn("Energy Sword") } } } },
            },
        };

        UnitFileEntry compiled = CompileOne(unit,
            new UpgradeChoice { SectionId = "all", OptionId = "o1", Count = 1 },
            new UpgradeChoice { SectionId = "sgt", OptionId = "o2", Count = 1 });

        Assert.That(compiled.Weapons.Single(w => w.Name == "Shard Carbine").Quantity, Is.EqualTo(5));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Energy Sword").Quantity, Is.EqualTo(1),
            "the sergeant's sword arrives after the all-swap and keeps it");
        Assert.That(compiled.PointCost, Is.EqualTo(130), "100 + 10 flat + 20 - the flat price is levied once");
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
        UnitFileEntry combined = army.Units.Single(u => u.Name.StartsWith("Vanguard Warriors"));
        Assert.That(combined.Name, Is.EqualTo("Vanguard Warriors"), "the merged pair keeps the plain unit name");
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

    // #261: OPR writes a swap's multiplicity into the target TEXT ("2x Rapid Shard Cannon"), not a field.
    // Nothing stripped it, so the whole string was matched against weapon names, matched nothing, and the
    // swap applied nothing and cost nothing while the Forge greyed the section out as "none to replace" -
    // on a unit visibly carrying the weapon. Modelled on High Elf Fleets' Anti-Gravity Tank.
    private static RosterUnit TwinCannonTank() => new()
    {
        Id = "tank", Name = "Tank", Quality = 3, Defense = 2,
        BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 520,
        Weapons = { Wpn("Rapid Shard Cannon", 2), Wpn("Mounted Shardguns") },
        Sections =
        {
            new UpgradeSection
            {
                Id = "twin", Label = "Replace 2x Rapid Shard Cannon", Variant = UpgradeVariant.Replace,
                Affects = UpgradeAffects.One, Targets = { "2x Rapid Shard Cannon" },
                Options = { new UpgradeOption { Id = "prism", Cost = 45, WeaponsGained = { Wpn("Prism Cannon") } } },
            },
        },
    };

    [Test]
    public void QuantityPrefixedTarget_ConsumesEveryCopy_AndIsCharged()
    {
        UnitFileEntry compiled = CompileOne(TwinCannonTank(),
            new UpgradeChoice { SectionId = "twin", OptionId = "prism", Count = 1 });

        Assert.That(compiled.Weapons.Any(w => w.Name == "Rapid Shard Cannon"), Is.False,
            "the swap consumes BOTH cannons - leaving one behind would be a phantom weapon");
        Assert.That(compiled.Weapons.Single(w => w.Name == "Prism Cannon").Quantity, Is.EqualTo(1));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Mounted Shardguns").Quantity, Is.EqualTo(1),
            "an untargeted weapon is untouched");
        Assert.That(compiled.PointCost, Is.EqualTo(565));                                     // 520 + 45
    }

    // The Forge greys a Replace out when this returns 0 - the user-visible half of the same bug: after
    // clearing the upgrade you could not pick it again, because the section reported nothing to replace.
    [Test]
    public void QuantityPrefixedTarget_ReportsTheSwapAsAvailable()
    {
        RosterUnit tank = TwinCannonTank();
        Assert.That(ListCompiler.AvailableApplications(tank.Weapons, tank.Items, tank.Sections[0].Targets),
            Is.EqualTo(1), "2 cannons afford exactly one 2x swap");

        // One cannon short of the prefix is genuinely not enough for a single application.
        tank.Weapons.Single(w => w.Name == "Rapid Shard Cannon").Quantity = 1;
        Assert.That(ListCompiler.AvailableApplications(tank.Weapons, tank.Items, tank.Sections[0].Targets),
            Is.EqualTo(0));
    }

    [TestCase("2x Rapid Shard Cannon", "Rapid Shard Cannon", 2)]
    [TestCase("3x Heavy Razor Claws", "Heavy Razor Claws", 3)]
    [TestCase("Rapid Shard Cannon", "Rapid Shard Cannon", 1)]
    [TestCase("Energy Swords", "Energy Swords", 1)]
    [TestCase("Suit-Burst", "Suit-Burst", 1)]
    // Not a prefix: no digits, or no space after the 'x'. A weapon whose name merely starts with an 'x'-ish
    // token must not be silently truncated.
    [TestCase("xeno Blade", "xeno Blade", 1)]
    [TestCase("2xRapid", "2xRapid", 1)]
    public void ParseTarget_SplitsTheQuantityPrefix(string target, string expectedName, int expectedPer)
    {
        (string name, int per) = ListCompiler.ParseTarget(target);
        Assert.That(name, Is.EqualTo(expectedName));
        Assert.That(per, Is.EqualTo(expectedPer));
    }
}
