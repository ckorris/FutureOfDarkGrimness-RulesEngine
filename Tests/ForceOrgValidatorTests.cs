using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #003 — Force organization validation. ForceOrgValidator is advisory only: it returns a warning
    // string per exceeded cap and never blocks. These tests pin each of the four GDF caps (cost / hero /
    // copy / unit) plus the clean and empty cases.
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

        private static ArmyListFile Army(int pointsLimit, params UnitFileEntry[] units)
            => new ArmyListFile { PointsLimit = pointsLimit, Units = units.ToList() };

        private static UnitFileEntry Unit(string name, int pointCost, bool hero = false)
        {
            UnitFileEntry unit = new UnitFileEntry
            {
                Name = name,
                ModelCount = 1,
                Quality = 4,
                Defense = 4,
                PointCost = pointCost,
            };
            if (hero)
            {
                unit.SpecialRules.Add(new SpecialRuleEntry_Core("Hero"));
            }
            return unit;
        }
    }
}
