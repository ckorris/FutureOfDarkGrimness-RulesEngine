using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #225 — the rectangular-base orientation migration. The importer now maps OPR's "LxW" so that length
// lands on HeightInches (the facing axis), but the bundled books/armies were emitted under the old
// positional mapping. These pin the retrofit that corrects them in place.
[TestFixture]
public class BaseOrientationRetrofitTests
{
    private static BaseFileEntry RectBase(float widthIn, float heightIn) => new()
    {
        Shape = EBaseShapeKind.Rectangle,
        WidthInches = widthIn,
        HeightInches = heightIn,
    };

    // A book carries RosterUnit; a compiled army carries UnitFileEntry. Both expose the same Base.
    private static RosterUnit BookRect(string name, float widthIn, float heightIn) =>
        new() { Name = name, Base = RectBase(widthIn, heightIn) };

    private static RosterUnit BookCircle(string name, float diameterIn) => new()
    {
        Name = name,
        Base = new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = diameterIn },
    };

    private static UnitFileEntry ArmyRect(string name, float widthIn, float heightIn) =>
        new() { Name = name, Base = RectBase(widthIn, heightIn) };

    [Test]
    public void ApplyToBook_SwapsAMisorientedRectangle_SoLengthRunsAlongTheFacing()
    {
        // A 60x35 bike base emitted the old way: 60 (length) sat on Width, so the model faced across
        // its 35mm axis.
        var book = new BookFile { Name = "B" };
        book.Units.Add(BookRect("Bikers", widthIn: 60f / 25.4f, heightIn: 35f / 25.4f));

        Assert.That(BaseOrientationRetrofit.ApplyToBook(book), Is.True);

        BaseFileEntry fixedBase = book.Units[0].Base;
        Assert.That(fixedBase.HeightInches, Is.EqualTo(60f / 25.4f).Within(0.001f),
            "length must end up on the facing axis");
        Assert.That(fixedBase.WidthInches, Is.EqualTo(35f / 25.4f).Within(0.001f));
    }

    [Test]
    public void ApplyToBook_IsIdempotent_ASecondRunChangesNothing()
    {
        var book = new BookFile { Name = "B" };
        book.Units.Add(BookRect("Bikers", widthIn: 60f / 25.4f, heightIn: 35f / 25.4f));

        Assert.That(BaseOrientationRetrofit.ApplyToBook(book), Is.True, "first run corrects it");
        Assert.That(BaseOrientationRetrofit.ApplyToBook(book), Is.False, "second run must be a no-op");
        Assert.That(book.Units[0].Base.HeightInches, Is.EqualTo(60f / 25.4f).Within(0.001f));
    }

    [Test]
    public void ApplyToBook_LeavesCirclesAndSquaresAlone()
    {
        var book = new BookFile { Name = "B" };
        book.Units.Add(BookCircle("Warriors", 32f / 25.4f));
        book.Units.Add(BookRect("Square", widthIn: 2f, heightIn: 2f));
        book.Units.Add(BookRect("AlreadyCorrect", widthIn: 35f / 25.4f, heightIn: 60f / 25.4f));

        Assert.That(BaseOrientationRetrofit.ApplyToBook(book), Is.False);
        Assert.That(book.Units[0].Base.DiameterInches, Is.EqualTo(32f / 25.4f).Within(0.001f));
        Assert.That(book.Units[1].Base.WidthInches, Is.EqualTo(2f).Within(0.001f));
        Assert.That(book.Units[2].Base.HeightInches, Is.EqualTo(60f / 25.4f).Within(0.001f));
    }

    [Test]
    public void ApplyToArmy_AlsoFixesAForgeArmysEmbeddedBookSnapshot()
    {
        // #236: a forge army carries the book it was built from. Fixing only the compiled units would
        // leave the snapshot stale, and re-opening the list in the builder would reintroduce the swap.
        var army = new BuiltArmyFile { Name = "A", Book = new BookFile { Name = "B" } };
        army.Units.Add(ArmyRect("Bikers", widthIn: 60f / 25.4f, heightIn: 35f / 25.4f));
        army.Book!.Units.Add(BookRect("Bikers", widthIn: 60f / 25.4f, heightIn: 35f / 25.4f));

        Assert.That(BaseOrientationRetrofit.ApplyToArmy(army), Is.True);
        Assert.That(army.Units[0].Base.HeightInches, Is.EqualTo(60f / 25.4f).Within(0.001f));
        Assert.That(army.Book!.Units[0].Base.HeightInches, Is.EqualTo(60f / 25.4f).Within(0.001f),
            "the embedded book snapshot must be migrated too");
    }

    // --- #225 defect B: the unsized 28mm default -------------------------------------------------

    [Test]
    public void ApplyToBook_ReplacesTheUnsized28mmDefault_WithAnEstimate()
    {
        // A Tough(24) titan that OPR declared no base for: it used to collide as a 28mm dot.
        var book = new BookFile { Name = "B" };
        var titan = new RosterUnit { Name = "Dread Titan", Base = new BaseFileEntry() };
        titan.Rules.Add(new SpecialRuleEntry_CoreNumeric("Tough", 24));
        book.Units.Add(titan);

        var estimates = new List<string>();
        Assert.That(BaseOrientationRetrofit.ApplyToBook(book, estimates), Is.True);

        Assert.That(titan.Base.Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(titan.Base.HeightInches, Is.EqualTo(175f / 25.4f).Within(0.001f));
        Assert.That(estimates, Has.Count.EqualTo(1), "an invented base must be reported, never silent");
        Assert.That(estimates[0], Does.Contain("Dread Titan"));
    }

    [Test]
    public void ApplyToBook_BothDefectsTogether_IsStillIdempotent()
    {
        // One unsized default + one mis-oriented rectangle in the same book: after one pass both are
        // correct, and a second pass must change nothing (an estimate must not itself look unsized).
        var book = new BookFile { Name = "B" };
        var tank = new RosterUnit { Name = "Battle Tank", Base = new BaseFileEntry() };
        tank.Rules.Add(new SpecialRuleEntry_CoreNumeric("Tough", 12));
        book.Units.Add(tank);
        book.Units.Add(BookRect("Bikers", widthIn: 60f / 25.4f, heightIn: 35f / 25.4f));

        Assert.That(BaseOrientationRetrofit.ApplyToBook(book), Is.True, "first run fixes both");
        Assert.That(BaseOrientationRetrofit.ApplyToBook(book), Is.False, "second run must be a no-op");

        Assert.That(tank.Base.HeightInches, Is.EqualTo(120f / 25.4f).Within(0.001f));
        Assert.That(book.Units[1].Base.HeightInches, Is.EqualTo(60f / 25.4f).Within(0.001f));
    }

    [Test]
    public void ApplyToArmy_EstimatesFromTheCompiledUnitsOwnRules()
    {
        var army = new ArmyListFile { Name = "A" };
        var apc = new UnitFileEntry { Name = "APC", Base = new BaseFileEntry() };
        apc.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric("Tough", 6));
        army.Units.Add(apc);

        Assert.That(BaseOrientationRetrofit.ApplyToArmy(army), Is.True);
        Assert.That(apc.Base.Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
        Assert.That(apc.Base.HeightInches, Is.EqualTo(90f / 25.4f).Within(0.001f));
    }

    [Test]
    public void ApplyToArmy_HandAuthoredArmyWithNoBookSnapshot_StillMigrates()
    {
        var army = new ArmyListFile { Name = "A" };
        army.Units.Add(ArmyRect("Bikers", widthIn: 60f / 25.4f, heightIn: 35f / 25.4f));

        Assert.That(BaseOrientationRetrofit.ApplyToArmy(army), Is.True);
        Assert.That(army.Units[0].Base.HeightInches, Is.EqualTo(60f / 25.4f).Within(0.001f));
    }
}
