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
        // #430: the copy cap is 1+X per 1000 pts (3 at 2000), so the list needs a real limit to trip it.
        var issues = Check(DemoBook.Build(), List(2000, U("gunners"), U("gunners"), U("gunners"), U("gunners")));
        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("copies")), Is.True);
    }

    // #438: the 3.5 "Unique" fixtures (TwoCopiesOfUniqueUnit_IsAnError, OneCopyOfUniqueUnit_NoUniqueIssue,
    // DuplicatesOfNonUniqueUnit_NoUniqueIssue) retired with the rule - the Unique Hero tests below pin the 3.6 rule.

    // ── #430 Unique Hero (3.6: "Counts as having Hero, and this model may only be taken once per army.") ──

    private static BookFile UniqueHeroBook(string? gameSystem = null, int heroTough = 3) => new()
    {
        Name = "UH",
        GameSystem = gameSystem,
        Units =
        {
            new RosterUnit
            {
                Id = "drekhor", Name = "Drekhor", Quality = 3, Defense = 4,
                BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 80,
                Rules = { new SpecialRuleEntry_Core("Unique Hero"), new SpecialRuleEntry_CoreNumeric("Tough", heroTough) },
            },
            new RosterUnit
            {
                Id = "karaz", Name = "Karaz", Quality = 3, Defense = 4,
                BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 80,
                Rules = { new SpecialRuleEntry_Core("Unique Hero") },
            },
            new RosterUnit
            {
                Id = "squad", Name = "Squad", Quality = 4, Defense = 4,
                BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
            },
        },
    };

    [Test]
    public void TwoCopiesOfUniqueHero_IsAnError_OnTheSecondCopy_AndNotAlsoAWarning()
    {
        var issues = Check(UniqueHeroBook(), List(2000, U("drekhor"), U("drekhor"), U("squad")));

        ListIssue error = issues.Single(i => i.Severity == ListIssueSeverity.Error && i.Message.Contains("Unique Hero"));
        Assert.That(error.UnitIndex, Is.EqualTo(1));
        Assert.That(error.Message, Does.Contain("Drekhor").And.Contain("one copy"));
        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("Unique Hero")),
            Is.False, "the force-org name-based duplicate warning is dropped in favour of the Error");
    }

    [Test]
    public void OneCopyEachOfTwoUniqueHeroes_NoUniqueIssue()
    {
        var issues = Check(UniqueHeroBook(), List(2000, U("drekhor"), U("karaz"), U("squad")));

        Assert.That(issues.Any(i => i.Message.Contains("Unique")), Is.False);
    }

    [Test]
    public void UniqueHero_CountsTowardTheHeroCap()
    {
        // 500 pts allows one Hero: two different Unique Heroes (no Hero rule on either) are over.
        var over = Check(UniqueHeroBook(), List(500, U("drekhor"), U("karaz"), U("squad")));
        var ok = Check(UniqueHeroBook(), List(500, U("drekhor"), U("squad")));

        Assert.That(over.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("Too many Heroes: 2")), Is.True);
        Assert.That(ok.Any(i => i.Message.Contains("Too many Heroes")), Is.False);
    }

    [Test]
    public void UniqueHero_CanJoin_AndTheJoinToughCapApplies()
    {
        var joined = List(2000,
            new BuilderUnit { RosterUnitId = "drekhor", Id = "h1", JoinsUnitId = "s1" },
            new BuilderUnit { RosterUnitId = "squad", Id = "s1" });

        var fine = Check(UniqueHeroBook(heroTough: 3), joined);
        Assert.That(fine.Any(i => i.Message.Contains("no Hero rule")), Is.False, "it is a Hero for the join check");
        Assert.That(fine, Is.Empty);

        var tooTough = Check(UniqueHeroBook(heroTough: 9), joined);
        Assert.That(tooTough.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("Tough(9)")), Is.True,
            "the Hero Tough(6) join cap reads a Unique Hero too");
    }

    // ── #430 force-org limits follow the book's game system ──────────────────────────────────────────────

    [Test]
    public void ForceOrgWarnings_UseTheBooksGameSystem()
    {
        // Six 100-pt squads at 1000 pts: GDF allows 1000/200 = 5 units, AoF 1000/150 = 6.
        BuilderUnit[] sixSquads = Enumerable.Range(0, 6).Select(_ => U("squad")).ToArray();

        var gdf = Check(UniqueHeroBook(), List(1000, sixSquads));
        var aof = Check(UniqueHeroBook("age-of-fantasy"), List(1000, sixSquads));

        Assert.That(gdf.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("Too many units: 6 (max 5")), Is.True);
        Assert.That(aof.Any(i => i.Message.Contains("Too many units")), Is.False);
    }

    [Test]
    public void CombinedPair_IsOneCopyAndOneUnit_At36Limits()
    {
        // Two combined pairs of Warriors at 1000 pts (3.6 copy cap 1+1 = 2): two squads, not four.
        var ok = Check(DemoBook.Build(), List(1000,
            Warriors("a"), Combined("b", "a"), Warriors("c"), Combined("d", "c")));
        Assert.That(ok.Any(i => i.Message.Contains("Too many copies")), Is.False);

        var over = Check(DemoBook.Build(), List(1000,
            Warriors("a"), Combined("b", "a"), Warriors("c"), Combined("d", "c"), Warriors("e")));
        Assert.That(over.Any(i => i.Message.Contains("Too many copies")), Is.True, "a third squad is over the cap of 2");
    }

    // ── #107 combined squads ────────────────────────────────────────────────────────────────────────────

    private static BuilderUnit Combined(string id, string withId) =>
        new() { RosterUnitId = "warriors", Id = id, CombinedWithId = withId };

    private static BuilderUnit Warriors(string id) => new() { RosterUnitId = "warriors", Id = id };

    [Test]
    public void ValidCombinedPair_NoCombinedIssues_AndCountsAsOneSquadForComposition()
    {
        // Four warriors rows as two combined pairs = TWO squads on the table (Chris, 2026-07-10:
        // doubled-up squads must not count twice toward the copy cap) - no copy warning.
        var issues = Check(DemoBook.Build(), List(2000,
            Warriors("a"), Combined("b", "a"), Warriors("c"), Combined("d", "c")));

        Assert.That(issues.Any(i => i.Message.Contains("combine")), Is.False);
        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("copies")),
            Is.False, "a combined pair is one squad toward the copy cap");
    }

    [Test]
    public void FourCombinedPairs_ExceedTheCopyCap()
    {
        // Eight rows as four pairs = four squads: one over the 3-copy cap (1+X, X = 1 per 1000 pts: 3 at 2000).
        var issues = Check(DemoBook.Build(), List(2000,
            Warriors("a"), Combined("b", "a"), Warriors("c"), Combined("d", "c"),
            Warriors("e"), Combined("f", "e"), Warriors("g"), Combined("h", "g")));

        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("copies")),
            Is.True, "four squads of one unit exceed the cap even when each is a combined pair");
    }

    [Test]
    public void CombinedPairsAndPlainSquads_ShareOneCopyGroup()
    {
        // Two pairs + two plain squads = four squads of the same unit - over the cap; merged
        // pairs keep the plain unit name, so they fall into the same copy group naturally.
        var issues = Check(DemoBook.Build(), List(2000,
            Warriors("a"), Combined("b", "a"), Warriors("c"), Combined("d", "c"),
            Warriors("e"), Warriors("f")));

        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Warning && i.Message.Contains("copies")),
            Is.True, "pairs and plain squads of the same unit count into one group");
    }

    [Test]
    public void DanglingCombineTarget_IsAWarning()
    {
        var issues = Check(DemoBook.Build(), List(100000, Combined("a", "nope")));
        Assert.That(issues.Any(i => i.Severity == ListIssueSeverity.Warning
            && i.Message.Contains("combine target no longer exists")), Is.True);
    }

    [Test]
    public void CombiningAcrossRosterUnits_IsAWarning()
    {
        var gunners = new BuilderUnit { RosterUnitId = "gunners", Id = "g" };
        var issues = Check(DemoBook.Build(), List(100000, gunners, Combined("w", "g")));
        Assert.That(issues.Any(i => i.Message.Contains("SAME unit")), Is.True);
    }

    [Test]
    public void ThreeCopiesInOneCombine_IsAWarning()
    {
        var issues = Check(DemoBook.Build(), List(100000,
            Warriors("a"), Combined("b", "a"), Combined("c", "a")));
        Assert.That(issues.Any(i => i.Message.Contains("only two copies may combine")), Is.True);
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

    // ── #219 unpriced upgrades ──────────────────────────────────────────────────────────────────────────

    private static BookFile UnpricedBook() => new()
    {
        Name = "P",
        Units =
        {
            new RosterUnit
            {
                Id = "x", Name = "X", Quality = 4, Defense = 4, BaseModelCount = 1, MinModels = 1, MaxModels = 1,
                BasePointCost = 10,
                Sections =
                {
                    new UpgradeSection
                    {
                        Id = "s", Label = "Wargear", MaxPicks = 2,
                        Options =
                        {
                            new UpgradeOption { Id = "free", Label = "Free Blade", Cost = 0 },
                            new UpgradeOption { Id = "paid", Label = "Paid Blade", Cost = 15 },
                            new UpgradeOption { Id = "hidden", Label = "Hexer", Cost = 0, CostUnpriced = true },
                        },
                    },
                },
            },
        },
    };

    [Test]
    public void UnpricedUpgradeSelected_Warns()
    {
        var unit = U("x", new UpgradeChoice { SectionId = "s", OptionId = "hidden", Count = 1 });
        var issues = Check(UnpricedBook(), List(100000, unit));

        ListIssue warn = issues.Single(i => i.Severity == ListIssueSeverity.Warning
            && i.Message.Contains("no published points cost"));
        Assert.That(warn.Message, Does.Contain("Hexer"));
        Assert.That(warn.UnitIndex, Is.EqualTo(0));
    }

    [Test]
    public void FreeAndPricedUpgrades_DoNotWarn()
    {
        // A genuine 0-cost option (explicit free) and a normally-priced one must not trip the unpriced warning.
        var unit = U("x",
            new UpgradeChoice { SectionId = "s", OptionId = "free", Count = 1 },
            new UpgradeChoice { SectionId = "s", OptionId = "paid", Count = 1 });
        var issues = Check(UnpricedBook(), List(100000, unit));

        Assert.That(issues.Any(i => i.Message.Contains("no published points cost")), Is.False);
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
