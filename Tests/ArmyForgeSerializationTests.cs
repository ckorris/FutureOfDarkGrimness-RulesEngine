using System.Collections.Generic;
using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.ArmyBuilding;
using NUnit.Framework;

namespace FDG.Tests;

// #153 (P0) — the catalog/selection/embedded-file types round-trip through the same STJ kind-schema as a
// hand-authored .fdgarmy, and — the load-bearing claim of the whole feature — the engine reads a builder-saved
// file as a plain ArmyListFile, silently ignoring the embedded book + selections (so embedding needs no
// engine change).
[TestFixture]
public class ArmyForgeSerializationTests
{
    private static BookFile MakeBook() => new()
    {
        Name = "Demo Legion", Faction = "Demo", Version = "1.0.0",
        Units =
        {
            new RosterUnit
            {
                Id = "warriors", Name = "Warriors",
                Quality = 4, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 10, BasePointCost = 65,
                Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 5, RangeInches = 24, Attacks = 1 } },
                Rules = { new SpecialRuleEntry_CoreNumeric("Tough", 3) },
                Sections =
                {
                    new UpgradeSection
                    {
                        Id = "warriors-heavy", Label = "Upgrade one Rifle to a Heavy Rifle",
                        Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One, Targets = { "Rifle" },
                        Options =
                        {
                            new UpgradeOption
                            {
                                Id = "heavy-rifle", Label = "Heavy Rifle (36\", A1, AP(1))", Cost = 5,
                                WeaponsGained =
                                {
                                    new WeaponFileEntry
                                    {
                                        Name = "Heavy Rifle", Quantity = 1, RangeInches = 36, Attacks = 1,
                                        SpecialRules = { new SpecialRuleEntry_CoreNumeric("AP", 1) },
                                    },
                                },
                            },
                        },
                    },
                },
            },
            new RosterUnit
            {
                Id = "gunners", Name = "Heavy Gunners",
                Quality = 4, Defense = 4, BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 120,
                Weapons = { new WeaponFileEntry { Name = "Heavy Rifle", Quantity = 3, RangeInches = 36, Attacks = 1 } },
            },
        },
    };

    private static BuiltArmyFile MakeBuiltArmy()
    {
        BookFile book = MakeBook();
        return new BuiltArmyFile
        {
            Name = "My List", Faction = "Demo", PointsLimit = 500,
            // A representative compiled unit (the real compiler produces these in the P0 compiler slice).
            Units =
            {
                new UnitFileEntry
                {
                    Name = "Warriors", ModelCount = 5, Quality = 4, Defense = 4, PointCost = 70,
                    SpecialRules = { new SpecialRuleEntry_CoreNumeric("Tough", 3) },
                    Weapons =
                    {
                        new WeaponFileEntry { Name = "Rifle", Quantity = 4, RangeInches = 24, Attacks = 1 },
                        new WeaponFileEntry
                        {
                            Name = "Heavy Rifle", Quantity = 1, RangeInches = 36, Attacks = 1,
                            SpecialRules = { new SpecialRuleEntry_CoreNumeric("AP", 1) },
                        },
                    },
                },
            },
            Selections = new BuilderList
            {
                BookName = "Demo Legion", PointsLimit = 500,
                Units = { new BuilderUnit
                {
                    RosterUnitId = "warriors", ModelCount = 5,
                    Choices = { new UpgradeChoice { SectionId = "warriors-heavy", OptionId = "heavy-rifle", Count = 1 } },
                } },
            },
            Book = book,
        };
    }

    [Test]
    public void BuiltArmyFile_RoundTripsStructurally_AsDerivedType()
    {
        BuiltArmyFile built = MakeBuiltArmy();

        string first = JsonSerializer.Serialize(built, RuleJson.Options);
        BuiltArmyFile back = JsonSerializer.Deserialize<BuiltArmyFile>(first, RuleJson.Options)!;
        string second = JsonSerializer.Serialize(back, RuleJson.Options);

        Assert.That(second, Is.EqualTo(first), "built army did not round-trip structurally.");

        // Base (playable) view survived.
        Assert.That(back.Units, Has.Count.EqualTo(1));
        Assert.That(back.Units[0].Weapons, Has.Count.EqualTo(2));

        // Embedded editable state survived, including polymorphic rule entries and the full book snapshot.
        Assert.That(back.Selections, Is.Not.Null);
        Assert.That(back.Selections!.Units[0].RosterUnitId, Is.EqualTo("warriors"));
        Assert.That(back.Selections.Units[0].Choices[0].OptionId, Is.EqualTo("heavy-rifle"));
        Assert.That(back.Book, Is.Not.Null);
        Assert.That(back.Book!.Units, Has.Count.EqualTo(2));
        Assert.That(back.Book.Units[0].Rules[0], Is.EqualTo(new SpecialRuleEntry_CoreNumeric("Tough", 3)));
        Assert.That(back.Book.Units[0].Sections[0].Options[0].Cost, Is.EqualTo(5));
    }

    [Test]
    public void Engine_ReadsBuilderFile_AsPlainArmy_IgnoringEmbeddedBlock()
    {
        // The exact bytes the builder writes.
        string json = JsonSerializer.Serialize(MakeBuiltArmy(), RuleJson.Options);

        // The engine / lobby / headless loader all deserialize as ArmyListFile.
        ArmyListFile asArmy = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)!;

        Assert.That(asArmy, Is.Not.Null);
        Assert.That(asArmy.Units, Has.Count.EqualTo(1), "playable units must survive for the engine.");
        Assert.That(asArmy.Units[0].Name, Is.EqualTo("Warriors"));

        // Re-serializing the engine's view carries none of the embedded block — it was dropped on read.
        string engineView = JsonSerializer.Serialize(asArmy, RuleJson.Options);
        Assert.That(engineView, Does.Not.Contain("\"selections\""));
        Assert.That(engineView, Does.Not.Contain("\"book\""));
    }

    [Test]
    public void PlainArmyFile_DeserializesAsBuiltArmy_WithNullSelections()
    {
        // A hand-authored .fdgarmy (no embedded block) opened via the builder's derived type is graceful:
        // it still carries its playable units; it just isn't catalog-re-editable.
        var plain = new ArmyListFile { Name = "Hand", PointsLimit = 300,
            Units = { new UnitFileEntry { Name = "Grunts", ModelCount = 3, Quality = 5, Defense = 5, PointCost = 45 } } };
        string json = JsonSerializer.Serialize(plain, RuleJson.Options);

        BuiltArmyFile asBuilt = JsonSerializer.Deserialize<BuiltArmyFile>(json, RuleJson.Options)!;

        Assert.That(asBuilt.Units, Has.Count.EqualTo(1));
        Assert.That(asBuilt.Selections, Is.Null);
        Assert.That(asBuilt.Book, Is.Null);
    }

    // ── #356: an imported army saved verbatim can still carry an editable session ────────────────────────

    /// <summary>An Army Forge "Save As" army: verbatim OPR data, including the fields a field-by-field copy
    /// is most likely to forget (unattributed points, effect-set defaults, auxiliary units).</summary>
    private static ArmyListFile MakeImportedArmy() => new()
    {
        Name = "Imported", Faction = "Demo", PointsLimit = 500,
        UnattributedPoints = 15,
        DefaultRangedEffectSet = "laser", DefaultMeleeEffectSet = "blade",
        AuxiliaryUnits = new() { new UnitFileEntry { Name = "Spores", ModelCount = 5, PointCost = 0 } },
        RuleDefinitions = { new SpecialRuleDefinition("Demo Rule", new List<HookEntry>(), new List<ActivatedAbility>()) },
        Units =
        {
            new UnitFileEntry { Name = "Warriors", ModelCount = 5, Quality = 4, Defense = 4, PointCost = 70 },
            new UnitFileEntry { Name = "Heavy Gunners", ModelCount = 3, Quality = 4, Defense = 4, PointCost = 120 },
        },
    };

    private static BuilderList MakeSelections() => new()
    {
        BookName = "Demo Legion", PointsLimit = 500,
        Units =
        {
            new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5 },
            new BuilderUnit { RosterUnitId = "gunners", ModelCount = 3 },
        },
    };

    [Test]
    public void Attach_KeepsThePlayableArmyIntact_AndAddsTheEditableSession()
    {
        ArmyListFile imported = MakeImportedArmy();

        BuiltArmyFile attached = EditableSession.Attach(imported, MakeSelections(), MakeBook());

        // The playable half is what plays and what it costs - it must survive untouched, including the
        // optional fields a hand-written copy would drop.
        Assert.That(attached.Name, Is.EqualTo("Imported"));
        Assert.That(attached.Faction, Is.EqualTo("Demo"));
        Assert.That(attached.PointsLimit, Is.EqualTo(500));
        Assert.That(attached.Units, Has.Count.EqualTo(2));
        Assert.That(attached.TotalPoints, Is.EqualTo(imported.TotalPoints));
        Assert.That(attached.UnattributedPoints, Is.EqualTo(15));
        Assert.That(attached.DefaultRangedEffectSet, Is.EqualTo("laser"));
        Assert.That(attached.DefaultMeleeEffectSet, Is.EqualTo("blade"));
        Assert.That(attached.AuxiliaryUnits, Is.Not.Null.And.Count.EqualTo(1));
        Assert.That(attached.RuleDefinitions, Has.Count.EqualTo(1));

        Assert.That(attached.Selections, Is.Not.Null);
        Assert.That(attached.Book, Is.Not.Null);
    }

    [Test]
    public void Attach_Output_SerializesItsEmbeddedBlock_WhenWrittenAtRuntimeType()
    {
        BuiltArmyFile attached = EditableSession.Attach(MakeImportedArmy(), MakeSelections(), MakeBook());

        // The trap this guards: serializing through a base-typed reference silently writes only the base
        // properties, so the file would look saved and reopen as un-editable.
        string json = JsonSerializer.Serialize(attached, attached.GetType(), RuleJson.Options);

        Assert.That(json, Does.Contain("\"selections\""));
        Assert.That(json, Does.Contain("\"book\""));
        BuiltArmyFile back = JsonSerializer.Deserialize<BuiltArmyFile>(json, RuleJson.Options)!;
        Assert.That(back.Selections!.Units, Has.Count.EqualTo(2));
        Assert.That(back.UnattributedPoints, Is.EqualTo(15));
    }

    [Test]
    public void Measure_ReturnsNull_WhenTheFileHasNoEditableSession()
    {
        var plain = new BuiltArmyFile { Name = "Hand" };
        Assert.That(EditableSession.Measure(plain), Is.Null);
    }

    [Test]
    public void Measure_ReportsNoDrift_ForAForgeAuthoredFile()
    {
        // Both halves came from one compile, so reopening reproduces the army exactly - adopt silently.
        BookFile book = MakeBook();
        BuiltArmyFile forgeAuthored = ListCompiler.Compile(book, MakeSelections());

        EditableSessionDrift? drift = EditableSession.Measure(forgeAuthored);

        Assert.That(drift, Is.Not.Null);
        Assert.That(drift!.Differs, Is.False,
            $"saved {drift.SavedUnitCount} units/{drift.SavedPoints} pts vs rebuilt " +
            $"{drift.RebuiltUnitCount}/{drift.RebuiltPoints}");
        Assert.That(drift.DroppedUnits, Is.Empty);
    }

    [Test]
    public void Measure_ReportsPointsDrift_WhenOnlyThePricingDisagrees()
    {
        // The real Save As case (#219): same units both ways, but Army Forge's authoritative total carries
        // points for options it publishes no price for, so our rebuild reads lighter.
        BookFile book = MakeBook();
        BuiltArmyFile compiled = ListCompiler.Compile(book, MakeSelections());
        ArmyListFile playable = MakeImportedArmy();
        playable.Units.Clear();
        foreach (UnitFileEntry u in compiled.Units) playable.Units.Add(u);

        EditableSessionDrift drift = EditableSession.Measure(
            EditableSession.Attach(playable, MakeSelections(), book))!;

        Assert.That(drift.DroppedUnits, Is.Empty, "no unit was lost - only the price differs");
        Assert.That(drift.SavedUnitCount, Is.EqualTo(drift.RebuiltUnitCount));
        Assert.That(drift.SavedPoints, Is.EqualTo(compiled.TotalPoints + 15));
        Assert.That(drift.RebuiltPoints, Is.EqualTo(compiled.TotalPoints));
        Assert.That(drift.Differs, Is.True);
    }

    [Test]
    public void Measure_NamesUnitsTheRebuildCannotProduce()
    {
        // A unit the bundled book does not know is excluded from the reconstruction (#241), so it is in the
        // army you play but not in the session you would edit.
        BookFile book = MakeBook();
        BuilderList shortSelections = MakeSelections();
        shortSelections.Units.RemoveAt(1); // drops "Heavy Gunners"

        EditableSessionDrift drift = EditableSession.Measure(
            EditableSession.Attach(MakeImportedArmy(), shortSelections, book))!;

        Assert.That(drift.Differs, Is.True);
        Assert.That(drift.DroppedUnits, Is.EqualTo(new[] { "Heavy Gunners" }));
        Assert.That(drift.SavedUnitCount, Is.EqualTo(2));
        Assert.That(drift.RebuiltUnitCount, Is.EqualTo(1));
    }

    [Test]
    public void Measure_CountsDuplicateNamesAsAMultiset()
    {
        // Two copies saved, one rebuilt: exactly one is reported dropped, not zero and not two.
        BookFile book = MakeBook();
        var twoCopies = new BuilderList
        {
            BookName = "Demo Legion", PointsLimit = 500,
            Units =
            {
                new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "a" },
                new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "b" },
            },
        };
        BuiltArmyFile saved = ListCompiler.Compile(book, twoCopies);

        var oneCopy = new BuilderList
        {
            BookName = "Demo Legion", PointsLimit = 500,
            Units = { new BuilderUnit { RosterUnitId = "warriors", ModelCount = 5, Id = "a" } },
        };
        EditableSessionDrift drift = EditableSession.Measure(
            EditableSession.Attach(saved, oneCopy, book))!;

        Assert.That(drift.DroppedUnits, Has.Count.EqualTo(1));
        Assert.That(drift.DroppedUnits[0], Is.EqualTo("Warriors"));
    }
}
