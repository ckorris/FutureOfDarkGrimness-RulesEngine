using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #003 — Force organization validation. ForceOrgValidator is advisory only: it returns a warning
    // string per exceeded cap and never blocks. These tests pin each of the four GDF caps (cost / hero /
    // copy / unit) plus the clean and empty cases.
    //
    // #430 - the 3.6 limits, per game system (GDF and AoF print different numbers): every cap below has a
    // table-driven pair of cases per system - exactly AT the allowance (no warning) and ONE OVER (warning).
    // Core Rules v3.6.0, Force Org. (optional): GF 1 Hero per 500pts, X = 1 per 1000pts, 35%, 1 unit per
    // 200pts, 1 model or Tough per 20pts; AoF 375 / 750 / 35% / 150 / 15.
    [TestFixture]
    public class ForceOrgValidatorTests
    {
        [Test]
        public void CleanArmy_WithinAllCaps_NoWarnings()
        {
            ArmyListFile army = Army(1000,
                Unit("Warriors", 200),
                Unit("Heavy Gunners", 300),
                Unit("Captain", 150, hero: true));

            Assert.That(ForceOrgValidator.Validate(army), Is.Empty);
        }

        [Test]
        public void EmptyArmy_NoWarnings()
        {
            // A brand-new empty list (the Army Builder's starting state) must not nag.
            Assert.That(ForceOrgValidator.Validate(Army(1000)), Is.Empty);
        }

        [Test]
        public void OverPointsLimit_WarnsCost()
        {
            ArmyListFile army = Army(500, Unit("Warriors", 300), Unit("Tank", 400));

            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(army);

            Assert.That(warnings, Has.Exactly(1).Contains("Over points limit"));
        }

        [Test]
        public void AtExactlyPointsLimit_NoCostWarning()
        {
            // Boundary: equal to the limit is fine; only strictly-over warns.
            ArmyListFile army = Army(500, Unit("Warriors", 200), Unit("Gunners", 300));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Over points limit")), Is.False);
        }

        [Test]
        public void TooManyHeroes_WarnsHero()
        {
            // 1000 pts allows 2 heroes; this army has 3.
            ArmyListFile army = Army(1000,
                Unit("Warriors", 100),
                Unit("Captain", 50, hero: true),
                Unit("Lieutenant", 50, hero: true),
                Unit("Sergeant", 50, hero: true));

            Assert.That(ForceOrgValidator.Validate(army), Has.Exactly(1).Contains("Too many Heroes"));
        }

        [Test]
        public void HeroesAtAllowance_NoHeroWarning()
        {
            // Exactly 2 heroes at 1000 pts is allowed.
            ArmyListFile army = Army(1000,
                Unit("Warriors", 100),
                Unit("Captain", 50, hero: true),
                Unit("Lieutenant", 50, hero: true));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Too many Heroes")), Is.False);
        }

        [Test]
        public void TooManyCopies_WarnsCopy()
        {
            ArmyListFile army = Army(2000,
                Unit("Warriors", 100),
                Unit("Warriors", 100),
                Unit("Warriors", 100),
                Unit("Warriors", 100));

            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(army);

            Assert.That(warnings, Has.Exactly(1).Contains("Too many copies of \"Warriors\""));
        }

        [Test]
        public void LegacyCombinedSuffix_GroupsWithPlainSquads()
        {
            // Army files compiled before 2026-07-24 carry the old "X (Combined)" merge suffix;
            // such an entry must still count into the same copy group as plain "X" squads.
            ArmyListFile army = Army(2000,
                Unit("Warriors", 100),
                Unit("Warriors", 100),
                Unit("Warriors", 100),
                Unit("Warriors (Combined)", 200));

            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(army);

            Assert.That(warnings, Has.Exactly(1).Contains("Too many copies of \"Warriors\""));
        }

        [Test]
        public void BlankNamedUnits_DoNotCountAsDuplicates()
        {
            // Four unnamed (incomplete) entries must not read as 4 copies of the same unit.
            ArmyListFile army = Army(2000, Unit("", 100), Unit("", 100), Unit("", 100), Unit("", 100));

            Assert.That(ForceOrgValidator.Validate(army).Any(w => w.Contains("Too many copies")), Is.False);
        }

        [Test]
        public void AllHeroes_WarnsNoNonHeroUnits()
        {
            ArmyListFile army = Army(1500,
                Unit("Captain", 100, hero: true),
                Unit("Lieutenant", 100, hero: true));

            Assert.That(ForceOrgValidator.Validate(army), Has.Exactly(1).Contains("no non-Hero units"));
        }

        [Test]
        public void MultipleViolations_AllReported()
        {
            // Over points AND too many heroes AND no non-hero units, all at once.
            ArmyListFile army = Army(500,
                Unit("Captain", 300, hero: true),
                Unit("Lieutenant", 300, hero: true));

            IReadOnlyList<string> warnings = ForceOrgValidator.Validate(army);

            Assert.That(warnings.Any(w => w.Contains("Over points limit")), Is.True);
            Assert.That(warnings.Any(w => w.Contains("Too many Heroes")), Is.True);
            Assert.That(warnings.Any(w => w.Contains("no non-Hero units")), Is.True);
        }

        [Test]
        public void IsHero_DetectsHeroRuleThroughAlias()
        {
            UnitFileEntry plain = Unit("Warriors", 100);
            UnitFileEntry hero = Unit("Captain", 100, hero: true);
            UnitFileEntry aliasedHero = Unit("Warlord", 100);
            aliasedHero.SpecialRules.Add(new SpecialRuleEntry_Alias("Big Boss", new SpecialRuleEntry_Core("Hero")));

            Assert.That(ForceOrgValidator.IsHero(plain), Is.False);
            Assert.That(ForceOrgValidator.IsHero(hero), Is.True);
            Assert.That(ForceOrgValidator.IsHero(aliasedHero), Is.True, "Hero granted via an alias entry must still count.");
        }

        // ---- #430: 3.6 caps per game system ------------------------------------------------------------

        private const string Gdf = "grimdark-future";
        private const string Aof = "age-of-fantasy";

        // Hero cap: floor(limit / 500) GDF, floor(limit / 375) AoF. The 999 / 749 rows show the floor.
        [TestCase(null, 500, 1, false)]
        [TestCase(null, 500, 2, true)]
        [TestCase(Gdf, 1000, 2, false)]
        [TestCase(Gdf, 1000, 3, true)]
        [TestCase(null, 999, 1, false)]
        [TestCase(null, 999, 2, true)]
        [TestCase(Gdf, 1500, 3, false)]
        [TestCase(Gdf, 1500, 4, true)]
        [TestCase(Aof, 375, 1, false)]
        [TestCase(Aof, 375, 2, true)]
        [TestCase(Aof, 750, 2, false)]
        [TestCase(Aof, 750, 3, true)]
        [TestCase(Aof, 749, 1, false)]
        [TestCase(Aof, 749, 2, true)]
        [TestCase(Aof, 1500, 4, false)]
        [TestCase(Aof, 1500, 5, true)]
        public void HeroCap_IsOnePerNPoints_PerSystem(string? system, int limit, int heroes, bool expectWarning)
        {
            UnitFileEntry[] units = Enumerable.Range(0, heroes)
                .Select(i => Unit($"Hero {i}", 1, hero: true)).Append(Unit("Squad", 1)).ToArray();

            Assert.That(HasWarning(ArmyOf(system, limit, units), "Too many Heroes"), Is.EqualTo(expectWarning));
        }

        // Same limit, different system: 1500 pts allows 3 GDF Heroes but 4 AoF ones.
        [Test]
        public void HeroCap_SameLimit_DiffersBySystem()
        {
            UnitFileEntry[] units = Enumerable.Range(0, 4).Select(i => Unit($"Hero {i}", 1, hero: true))
                .Append(Unit("Squad", 1)).ToArray();

            Assert.That(HasWarning(ArmyOf(Gdf, 1500, units), "Too many Heroes"), Is.True);
            Assert.That(HasWarning(ArmyOf(Aof, 1500, units), "Too many Heroes"), Is.False);
        }

        // Copy cap: 1 + floor(limit / 1000) GDF, 1 + floor(limit / 750) AoF.
        [TestCase(null, 999, 1, false)]
        [TestCase(null, 999, 2, true)]
        [TestCase(Gdf, 1000, 2, false)]
        [TestCase(Gdf, 1000, 3, true)]
        [TestCase(Gdf, 1500, 2, false)]
        [TestCase(Gdf, 1500, 3, true)]
        [TestCase(Gdf, 2000, 3, false)]
        [TestCase(Gdf, 2000, 4, true)]
        [TestCase(Aof, 749, 1, false)]
        [TestCase(Aof, 749, 2, true)]
        [TestCase(Aof, 750, 2, false)]
        [TestCase(Aof, 750, 3, true)]
        [TestCase(Aof, 1500, 3, false)]
        [TestCase(Aof, 1500, 4, true)]
        public void CopyCap_IsOnePlusXPerNPoints_PerSystem(string? system, int limit, int copies, bool expectWarning)
        {
            UnitFileEntry[] units = Enumerable.Range(0, copies).Select(_ => Unit("Warriors", 1)).ToArray();

            Assert.That(HasWarning(ArmyOf(system, limit, units), "Too many copies of \"Warriors\""),
                Is.EqualTo(expectWarning));
        }

        [Test]
        public void CopyCap_SameLimit_DiffersBySystem()
        {
            // 1500 pts: GDF is 1 + 1 = 2 copies, AoF is 1 + 2 = 3.
            UnitFileEntry[] units = Enumerable.Range(0, 3).Select(_ => Unit("Warriors", 1)).ToArray();

            Assert.That(HasWarning(ArmyOf(Gdf, 1500, units), "Too many copies"), Is.True);
            Assert.That(HasWarning(ArmyOf(Aof, 1500, units), "Too many copies"), Is.False);
        }

        // "Combined units count as one": ListCompiler folds a combined pair into ONE army-file entry (the
        // merged unit carries both squads' models and points), so a pair is a single copy. Two merged pairs
        // at 1000 pts (cap 2) are two copies - fine - and a third plain squad makes three.
        [Test]
        public void CopyCap_CombinedPairCountsAsOneCopy()
        {
            UnitFileEntry pair1 = Unit("Warriors", 100, models: 10);
            UnitFileEntry pair2 = Unit("Warriors", 100, models: 10);

            Assert.That(HasWarning(ArmyOf(Gdf, 1000, pair1, pair2), "Too many copies"), Is.False);
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, pair1, pair2, Unit("Warriors", 50)), "Too many copies"), Is.True);
        }

        // Unit-size cap: no unit worth over 35% of the army's points, floor(limit * 35 / 100).
        [TestCase(null, 1000, 350, false)]
        [TestCase(null, 1000, 351, true)]
        [TestCase(Gdf, 2000, 700, false)]
        [TestCase(Gdf, 2000, 701, true)]
        [TestCase(Aof, 750, 262, false)]   // 35% of 750 = 262.5
        [TestCase(Aof, 750, 263, true)]
        [TestCase(Aof, 1500, 525, false)]
        [TestCase(Aof, 1500, 526, true)]
        public void UnitShareCap_Is35Percent_PerSystem(string? system, int limit, int unitCost, bool expectWarning)
        {
            ArmyListFile army = ArmyOf(system, limit, Unit("Big Unit", unitCost), Unit("Filler", 1));

            Assert.That(HasWarning(army, "Unit \"Big Unit\" is too large"), Is.EqualTo(expectWarning));
        }

        // A Hero who joins a unit is part of it: the joined pair is one unit worth the sum.
        [Test]
        public void UnitShareCap_CountsAJoinedHeroTowardItsHost()
        {
            UnitFileEntry squad = Unit("Squad", 300, models: 5);
            squad.Id = "s1";
            UnitFileEntry captain = Unit("Captain", 50, hero: true);
            captain.JoinsUnitId = "s1";

            // 300 + 50 = 350 = 35% of 1000: at the cap. The Hero stays a separate unit only when it doesn't join.
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, squad, captain, Unit("Filler", 1)), "is too large"), Is.False);

            captain.PointCost = 51;
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, squad, captain, Unit("Filler", 1)), "Unit \"Squad\" is too large"),
                Is.True, "351 for the joined unit is over");
        }

        // Unit-count cap: floor(limit / 200) GDF, floor(limit / 150) AoF.
        [TestCase(null, 999, 4, false)]
        [TestCase(null, 999, 5, true)]   // floor(999 / 200) = 4
        [TestCase(Gdf, 1000, 5, false)]
        [TestCase(Gdf, 1000, 6, true)]
        [TestCase(Gdf, 2000, 10, false)]
        [TestCase(Gdf, 2000, 11, true)]
        [TestCase(Aof, 750, 5, false)]
        [TestCase(Aof, 750, 6, true)]
        [TestCase(Aof, 1500, 10, false)]
        [TestCase(Aof, 1500, 11, true)]
        [TestCase(Gdf, 1500, 7, false)]
        [TestCase(Gdf, 1500, 8, true)]
        public void UnitCountCap_IsOnePerNPoints_PerSystem(string? system, int limit, int unitCount, bool expectWarning)
        {
            UnitFileEntry[] units = Enumerable.Range(0, unitCount).Select(i => Unit($"Unit {i}", 1)).ToArray();

            Assert.That(HasWarning(ArmyOf(system, limit, units), "Too many units"), Is.EqualTo(expectWarning));
        }

        [Test]
        public void UnitCountCap_AJoinedHeroIsNotAUnitOfItsOwn()
        {
            UnitFileEntry[] squads = Enumerable.Range(0, 5).Select(i => Unit($"Squad {i}", 1, models: 5)).ToArray();
            squads[0].Id = "s0";
            UnitFileEntry captain = Unit("Captain", 1, hero: true);
            captain.JoinsUnitId = "s0";
            UnitFileEntry[] units = squads.Append(captain).ToArray();

            // 5 squads + 1 Hero = 6 entries, but the Hero joins squad 0: 5 units on the table at 1000 pts (cap 5).
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, units), "Too many units"), Is.False);

            captain.JoinsUnitId = null;   // deploys solo -> a sixth unit
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, units), "Too many units"), Is.True);
        }

        // Model-or-Tough cap: floor(limit / 20) GDF, floor(limit / 15) AoF, a model counting max(1, Tough).
        [TestCase(null, 999, 49, false)]
        [TestCase(null, 999, 50, true)]   // floor(999 / 20) = 49
        [TestCase(Gdf, 1000, 50, false)]
        [TestCase(Gdf, 1000, 51, true)]
        [TestCase(Gdf, 2000, 100, false)]
        [TestCase(Gdf, 2000, 101, true)]
        [TestCase(Gdf, 1500, 75, false)]
        [TestCase(Gdf, 1500, 76, true)]
        [TestCase(Aof, 750, 50, false)]
        [TestCase(Aof, 750, 51, true)]
        [TestCase(Aof, 1500, 100, false)]
        [TestCase(Aof, 1500, 101, true)]
        [TestCase(Aof, 1499, 99, false)]
        [TestCase(Aof, 1499, 100, true)]  // floor(1499 / 15) = 99
        public void ModelCap_IsOneModelPerNPoints_PerSystem(string? system, int limit, int models, bool expectWarning)
        {
            Assert.That(HasWarning(ArmyOf(system, limit, Unit("Horde", 1, models: models)), "Too many models/Tough"),
                Is.EqualTo(expectWarning));
        }

        // OWNER REVIEW (#430 D1): a model with Tough(X) counts max(1, X), so one Tough(51) monster is as
        // heavy as 51 plain models, and a squad of Tough(3) models counts 3 per model.
        [Test]
        public void ModelCap_ToughCountsInPlaceOfTheModel()
        {
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, Unit("Titan", 1, models: 1, tough: 50)), "Too many models/Tough"),
                Is.False, "Tough(50) = 50, at the 1000 pt cap");
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, Unit("Titan", 1, models: 1, tough: 51)), "Too many models/Tough"),
                Is.True, "Tough(51) = 51, one over");

            Assert.That(HasWarning(ArmyOf(Gdf, 1000, Unit("Brutes", 1, models: 10, tough: 5)), "Too many models/Tough"),
                Is.False, "10 models x Tough(5) = 50");
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, Unit("Brutes", 1, models: 11, tough: 5)), "Too many models/Tough"),
                Is.True, "11 models x Tough(5) = 55");

            Assert.That(HasWarning(ArmyOf(Gdf, 1000, Unit("Squad", 1, models: 50, tough: 1)), "Too many models/Tough"),
                Is.False, "Tough(1) is no heavier than the model it sits on");
        }

        [Test]
        public void ModelCap_APerModelToughGrantCountsOnlyThatModel()
        {
            // #407: 20 plain models, one carrying Tough(5) of its own = 19 + 5 = 24 (not 100).
            UnitFileEntry squad = Unit("Squad", 1, models: 20);
            squad.ModelGrants = new List<ModelGrantEntry>
            {
                new ModelGrantEntry { Count = 1, Rules = { new SpecialRuleEntry_CoreNumeric("Tough", 5) } },
            };

            // 1000 pts -> 50, 24 is fine; 400 pts -> 20, 24 is over; 480 pts -> 24 exactly.
            Assert.That(HasWarning(ArmyOf(Gdf, 1000, squad), "Too many models/Tough"), Is.False);
            Assert.That(HasWarning(ArmyOf(Gdf, 480, squad), "Too many models/Tough"), Is.False, "24 at a 24 cap");
            Assert.That(HasWarning(ArmyOf(Gdf, 479, squad), "Too many models/Tough"), Is.True, "24 over a 23 cap");
        }

        [Test]
        public void ModelCap_AGrantNeverLowersTheUnitsOwnTough()
        {
            // Unit-wide Tough(3) on 4 models = 12; a grant of Tough(2) on one model does not lower it.
            UnitFileEntry squad = Unit("Squad", 1, models: 4, tough: 3);
            squad.ModelGrants = new List<ModelGrantEntry>
            {
                new ModelGrantEntry { Count = 1, Rules = { new SpecialRuleEntry_CoreNumeric("Tough", 2) } },
            };

            Assert.That(HasWarning(ArmyOf(Gdf, 240, squad), "Too many models/Tough"), Is.False, "12 at a 12 cap");
            Assert.That(HasWarning(ArmyOf(Gdf, 239, squad), "Too many models/Tough"), Is.True, "12 over an 11 cap");
        }

        [Test]
        public void SizeCaps_UseTheTotalWhenNoLimitIsSet()
        {
            // An imported list may carry no limit: the allowances then read the army's own total points.
            ArmyListFile army = ArmyOf(Gdf, 0, Unit("Squad", 1000, models: 10), Unit("Captain", 0, hero: true));

            Assert.That(HasWarning(army, "Too many Heroes"), Is.False, "1 Hero at 1000 pts of army");
            Assert.That(HasWarning(army, "is too large"), Is.True, "the 1000 pt squad is 100% of the army");
        }

        // ---- #430: Unique Hero ------------------------------------------------------------------------

        [Test]
        public void UniqueHero_CountsAsAHero_ForTheHeroCapAndIsHero()
        {
            UnitFileEntry unique = Unit("Drekhor", 100, uniqueHero: true);

            Assert.That(ForceOrgValidator.IsHero(unique), Is.True, "Unique Hero counts as having Hero");
            Assert.That(ForceOrgValidator.HasHeroRule(unique.SpecialRules), Is.True);

            // Two Unique Heroes at 500 pts (cap 1) is over the Hero cap, the same as two plain Heroes.
            ArmyListFile army = ArmyOf(Gdf, 500, unique, Unit("Karaz", 100, uniqueHero: true), Unit("Squad", 100, models: 5));
            Assert.That(HasWarning(army, "Too many Heroes: 2"), Is.True);
        }

        [Test]
        public void UniqueHero_ThroughAnAlias_StillCountsAsAHero()
        {
            UnitFileEntry boss = Unit("Boss", 100);
            boss.SpecialRules.Add(new SpecialRuleEntry_Alias("Big Boss", new SpecialRuleEntry_Core("Unique Hero")));

            Assert.That(ForceOrgValidator.IsHero(boss), Is.True);
        }

        [Test]
        public void UniqueHero_TakenTwice_WarnsForArmiesWithoutACatalog()
        {
            ArmyListFile twice = ArmyOf(Gdf, 2000, Unit("Drekhor", 100, uniqueHero: true),
                Unit("Drekhor", 100, uniqueHero: true), Unit("Squad", 100, models: 5));
            ArmyListFile once = ArmyOf(Gdf, 2000, Unit("Drekhor", 100, uniqueHero: true),
                Unit("Karaz", 100, uniqueHero: true), Unit("Squad", 100, models: 5));

            Assert.That(HasWarning(twice, "Unique Hero \"Drekhor\" taken 2 times"), Is.True);
            Assert.That(HasWarning(once, "taken"), Is.False, "two DIFFERENT Unique Heroes are fine");
        }

        [Test]
        public void ForceOrgLimits_Retarget_MapsStockSizesAndLeavesTypedOnesAlone()
        {
            ForceOrgLimits gdf = ForceOrgLimits.GrimdarkFuture;
            ForceOrgLimits aof = ForceOrgLimits.AgeOfFantasy;

            Assert.That(ForceOrgLimits.Retarget(2000, gdf, aof), Is.EqualTo(1500), "full -> full");
            Assert.That(ForceOrgLimits.Retarget(1000, gdf, aof), Is.EqualTo(750), "start -> start");
            Assert.That(ForceOrgLimits.Retarget(1500, aof, gdf), Is.EqualTo(2000));
            Assert.That(ForceOrgLimits.Retarget(750, aof, gdf), Is.EqualTo(1000));
            Assert.That(ForceOrgLimits.Retarget(1750, gdf, aof), Is.EqualTo(1750), "a typed size is not a preset");
            Assert.That(ForceOrgLimits.Retarget(2000, gdf, gdf), Is.EqualTo(2000));
        }

        [Test]
        public void ForceOrgLimits_For_AbsentAndUnknownSlugsReadAsGdf()
        {
            Assert.That(ForceOrgLimits.For((string?)null), Is.SameAs(ForceOrgLimits.GrimdarkFuture));
            Assert.That(ForceOrgLimits.For("opr-custom-tool"), Is.SameAs(ForceOrgLimits.GrimdarkFuture));
            Assert.That(ForceOrgLimits.For(Aof), Is.SameAs(ForceOrgLimits.AgeOfFantasy));
            Assert.That(ForceOrgLimits.For(EAllowedGameSystems.All), Is.SameAs(ForceOrgLimits.GrimdarkFuture));
            Assert.That(ForceOrgLimits.For(EAllowedGameSystems.AgeOfFantasy), Is.SameAs(ForceOrgLimits.AgeOfFantasy));
        }

        private static bool HasWarning(ArmyListFile army, string fragment) =>
            ForceOrgValidator.Validate(army).Any(w => w.Contains(fragment));

        private static ArmyListFile ArmyOf(string? gameSystem, int pointsLimit, params UnitFileEntry[] units)
            => new ArmyListFile { PointsLimit = pointsLimit, GameSystem = gameSystem, Units = units.ToList() };

        private static ArmyListFile Army(int pointsLimit, params UnitFileEntry[] units)
            => new ArmyListFile { PointsLimit = pointsLimit, Units = units.ToList() };

        private static UnitFileEntry Unit(string name, int pointCost, bool hero = false, int models = 1, int tough = 0,
            bool uniqueHero = false)
        {
            UnitFileEntry unit = new UnitFileEntry
            {
                Name = name,
                ModelCount = models,
                Quality = 4,
                Defense = 4,
                PointCost = pointCost,
            };
            if (hero)
            {
                unit.SpecialRules.Add(new SpecialRuleEntry_Core("Hero"));
            }
            if (uniqueHero)
            {
                unit.SpecialRules.Add(new SpecialRuleEntry_Core("Unique Hero"));
            }
            if (tough > 0)
            {
                unit.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric("Tough", tough));
            }
            return unit;
        }
    }
}
