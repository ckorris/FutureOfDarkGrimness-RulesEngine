using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #153 (P4) — legality checks for a catalog-built list: points limit + per-unit model-count range + per-section
// pick caps (catalog-specific), plus the reused ForceOrgValidator composition warnings.
[TestFixture]
public class ListValidatorTests
{
    private static IReadOnlyList<ListIssue> Check(BookFile book, BuilderList list) =>
        ListValidator.Validate(book, list, ListCompiler.Compile(book, list));

    private static BuilderList List(int limit, params BuilderUnit[] units)
    {
        var l = new BuilderList { PointsLimit = limit };
        l.Units.AddRange(units);
        return l;
    }

    private static BuilderUnit U(string id, params UpgradeChoice[] choices)
    {
        var bu = new BuilderUnit { RosterUnitId = id };
        bu.Choices.AddRange(choices);
        return bu;
    }

    [Test]
    public void LegalList_HasNoIssues()
    {
        var issues = Check(DemoBook.Build(), List(500, U("warriors"), U("gunners"))); // 185 pts, valid sizes
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void OverPointsLimit_IsAnError()
    {
        var issues = Check(DemoBook.Build(), List(50, U("warriors"))); // 65 > 50
        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Error && i.Message.Contains("points limit")), Is.True);
    }

    [Test]
    public void ModelCountOverMax_IsAnError()
    {
        // add-warrior ×10 → 5 + 10 = 15 models, but Warriors max is 10.
        var over = U("warriors", new UpgradeChoice { SectionId = "warriors-reinforce", OptionId = "add-warrior", Count = 10 });
        var issues = Check(DemoBook.Build(), List(100000, over));
        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Error && i.Message.Contains("models")), Is.True);
    }

    [Test]
    public void TooManyCopies_IsAForceOrgWarning()
    {
        var issues = Check(DemoBook.Build(), List(100000, U("gunners"), U("gunners"), U("gunners"), U("gunners")));
        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("copies")), Is.True);
    }

    // ── #006 hero-join checks ───────────────────────────────────────────────────────────────────────────

    private static BookFile JoinBook(int heroTough = 3) => new()
    {
        Name = "J",
        Units =
        {
            new RosterUnit
            {
                Id = "captain", Name = "Captain", Quality = 3, Defense = 4,
                BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 50,
                Rules = { new SpecialRuleEntry_Core("Hero"), new SpecialRuleEntry_CoreNumeric("Tough", heroTough) },
            },
            new RosterUnit
            {
                Id = "squad", Name = "Squad", Quality = 4, Defense = 4,
                BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
            },
            new RosterUnit
            {
                Id = "solo", Name = "Solo", Quality = 4, Defense = 4,
                BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 20,
            },
        },
    };

    [Test]
    public void HeroJoin_Valid_NoIssues_AndIdsCompileThrough()
    {
        var list = List(100000,
            new BuilderUnit { RosterUnitId = "captain", Id = "h1", JoinsUnitId = "s1" },
            new BuilderUnit { RosterUnitId = "squad", Id = "s1" });
        BuiltArmyFile compiled = ListCompiler.Compile(JoinBook(), list);

        Assert.That(ListValidator.Validate(JoinBook(), list, compiled), Is.Empty);
        // The join plumbing the engine reads at setup (#006) survives compilation.
        Assert.That(compiled.Units[0].JoinsUnitId, Is.EqualTo("s1"));
        Assert.That(compiled.Units[1].Id, Is.EqualTo("s1"));
    }

    [Test]
    public void HeroJoin_ToughOverCap_Warns()
    {
        var list = List(100000,
            new BuilderUnit { RosterUnitId = "captain", Id = "h1", JoinsUnitId = "s1" },
            new BuilderUnit { RosterUnitId = "squad", Id = "s1" });
        var issues = Check(JoinBook(heroTough: 9), list);

        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("Tough(9)")), Is.True);
    }

    [Test]
    public void HeroJoin_MissingTarget_Warns()
    {
        var list = List(100000, new BuilderUnit { RosterUnitId = "captain", Id = "h1", JoinsUnitId = "gone" });
        var issues = Check(JoinBook(), list);

        Assert.That(issues.Any(i => i.Message.Contains("no longer exists")), Is.True);
    }

    [Test]
    public void HeroJoin_SingleModelHost_Warns()
    {
        var list = List(100000,
            new BuilderUnit { RosterUnitId = "captain", Id = "h1", JoinsUnitId = "x1" },
            new BuilderUnit { RosterUnitId = "solo", Id = "x1" });
        var issues = Check(JoinBook(), list);

        Assert.That(issues.Any(i => i.Message.Contains("ineligible")), Is.True);
    }

    [Test]
    public void PickCapExceeded_IsAnError()
    {
        // A single-select section (MaxPicks 1) with two chosen options is illegal.
        var book = new BookFile
        {
            Name = "T",
            Units =
            {
                new RosterUnit
                {
                    Id = "x", Name = "X", Quality = 4, Defense = 4, BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 10,
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "s", Label = "Pick one", MaxPicks = 1,
                            Options = { new UpgradeOption { Id = "a" }, new UpgradeOption { Id = "b" } },
                        },
                    },
                },
            },
        };
        var unit = U("x",
            new UpgradeChoice { SectionId = "s", OptionId = "a", Count = 1 },
            new UpgradeChoice { SectionId = "s", OptionId = "b", Count = 1 });

        var issues = Check(book, List(100000, unit));
        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Error && i.Message.Contains("too many options")), Is.True);
    }
}
