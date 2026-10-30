using System.Collections.Generic;
using System.Linq;
using FDG.Calculator;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #397: the Combat Calculator runs the REAL combat stages in a throwaway world, so these tests are
    // as much a pin on that world being built correctly as on the arithmetic. Every case goes in through
    // ArmyListFile - the same door a real army walks through - so army creation, rule attachment and the
    // unit-creation rules are all exercised, not bypassed.
    //
    // Expected values are hand-computed from the rules: under the probabilistic roller a roll of N dice
    // needing X+ yields exactly N * (7-X)/6 successes, so every number below is checkable by eye.
    [TestFixture]
    public class CombatCalculatorTests
    {
        [Test]
        public void TenRifles_Quality4_IntoDefense4_IsFiveHitsAndHalfOfThemWound()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Shooters", models: 10, quality: 4, defense: 4, Rifle(quantity: 10))),
                Army(Unit("Targets", models: 10, quality: 4, defense: 4)),
                new CombatSituation(DistanceInches: 12f));

            Assert.That(report.Volleys, Has.Count.EqualTo(1));
            VolleyReport volley = report.Volleys[0];

            Assert.Multiple(() =>
            {
                Assert.That(volley.InRange, Is.True);
                Assert.That(volley.AttackDice, Is.EqualTo(10f).Within(Tolerance), "one die per carrier");
                Assert.That(volley.HitRollNeeded, Is.EqualTo(4), "Quality 4+");
                Assert.That(volley.ExpectedHits, Is.EqualTo(5f).Within(Tolerance), "10 dice on 4+");
                Assert.That(volley.Saves.Single().SaveNeeded, Is.EqualTo(4), "Defense 4+, no AP");
                Assert.That(report.ExpectedWounds, Is.EqualTo(2.5f).Within(Tolerance), "5 hits, half saved");
                Assert.That(report.DefenderWoundsBefore, Is.EqualTo(10f).Within(Tolerance));
                Assert.That(report.DefenderWoundsAfter, Is.EqualTo(7.5f).Within(Tolerance));
            });
        }

        [Test]
        public void Cover_ImprovesTheSaveByOne()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Shooters", 10, 4, 4, Rifle(10))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(DistanceInches: 12f, DefenderInCover: true));

            Assert.That(report.Volleys[0].Saves.Single().SaveNeeded, Is.EqualTo(3), "Defense 4+ with cover");
            // 5 hits, saved on 3+ (two thirds), so a third get through.
            Assert.That(report.ExpectedWounds, Is.EqualTo(5f / 3f).Within(Tolerance));
        }

        [Test]
        public void ArmorPenetration_WorsensTheSave()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Shooters", 10, 4, 4, Rifle(10, ap: 1))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(DistanceInches: 12f));

            Assert.That(report.Volleys[0].Saves.Single().SaveNeeded, Is.EqualTo(5), "Defense 4+ and AP(1)");
            // 5 hits, saved on 5+ (a third), so two thirds get through.
            Assert.That(report.ExpectedWounds, Is.EqualTo(5f * 4f / 6f).Within(Tolerance));
        }

        [Test]
        public void BeyondItsRange_TheWeaponFiresNothing()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Shooters", 10, 4, 4, Rifle(10, range: 24))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(DistanceInches: 30f));

            VolleyReport volley = report.Volleys.Single();
            Assert.Multiple(() =>
            {
                Assert.That(volley.InRange, Is.False);
                Assert.That(volley.EffectiveRangeInches, Is.EqualTo(24f).Within(Tolerance));
                Assert.That(volley.AttackDice, Is.EqualTo(0f).Within(Tolerance), "no dice are rolled at all");
                Assert.That(report.ExpectedWounds, Is.EqualTo(0f).Within(Tolerance));
                Assert.That(report.DefenderWoundsAfter, Is.EqualTo(report.DefenderWoundsBefore).Within(Tolerance));
            });
        }

        [Test]
        public void TheRequestedDistanceIsWhatTheRulesMeasure_StealthTurnsOnPastNineInches()
        {
            // Stealth is -1 to hit beyond 9". It reads the distance the placement produced, so this is a
            // pin on the geometry as much as on the rule: get the placement wrong and it fires on the
            // wrong side of the line.
            ArmyListFile shooters = Army(Unit("Shooters", 10, 4, 4, Rifle(10)));
            ArmyListFile sneaks = Army(Unit("Sneaks", 10, 4, 4, rules: new[] { "Stealth" }));

            CombatReport far = CombatCalculator.Run(shooters, sneaks, new CombatSituation(DistanceInches: 10f));
            CombatReport near = CombatCalculator.Run(shooters, sneaks, new CombatSituation(DistanceInches: 8f));

            Assert.Multiple(() =>
            {
                Assert.That(far.Volleys[0].HitRollNeeded, Is.EqualTo(5), "Stealth applies past 9in");
                Assert.That(near.Volleys[0].HitRollNeeded, Is.EqualTo(4), "and not within it");
                Assert.That(far.Volleys[0].ExpectedHits, Is.EqualTo(10f * 2f / 6f).Within(Tolerance));
                Assert.That(near.Volleys[0].ExpectedHits, Is.EqualTo(5f).Within(Tolerance));
            });
        }

        [Test]
        public void ToughIsApplied_SoAModelCanSoakMoreThanOneWound()
        {
            // The regression this guards: GameBootstrap.CreateArmy does NOT run the unit-creation rules,
            // so a calculator that forgets to would give every model exactly one wound and be wrong about
            // every tough unit in the game, silently.
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Shooters", 1, 4, 4, Rifle(1))),
                Army(Unit("Brute", models: 1, quality: 4, defense: 4, rules: new[] { "Tough(3)" })),
                new CombatSituation(DistanceInches: 12f));

            Assert.That(report.DefenderWoundsBefore, Is.EqualTo(3f).Within(Tolerance),
                "Tough(3) must survive army creation");
        }

        [Test]
        public void WoundsCarryBetweenWeapons_SoASecondWeaponCannotOverkill()
        {
            // One fragile model, two heavy weapons: the second fires into what the first left, and the
            // total can never exceed what there was to lose.
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Shooters", 10, 2, 4,
                    Rifle(10, attacks: 3, ap: 3, name: "Alpha Gun"),
                    Rifle(10, attacks: 3, ap: 3, name: "Beta Gun"))),
                Army(Unit("Victim", models: 1, quality: 6, defense: 6)),
                new CombatSituation(DistanceInches: 6f));

            Assert.Multiple(() =>
            {
                Assert.That(report.Volleys, Has.Count.EqualTo(2), "two profiles, two rows");
                Assert.That(report.ExpectedWounds, Is.LessThanOrEqualTo(1f + Tolerance),
                    "cannot deal more wounds than the target had");
                Assert.That(report.DefenderWoundsAfter, Is.GreaterThanOrEqualTo(-Tolerance));
                Assert.That(report.Volleys[1].ExpectedWounds, Is.LessThan(report.Volleys[0].ExpectedWounds),
                    "the second weapon finds less left to kill than the first did");
            });
        }

        [Test]
        public void WeaponRowsAreOrderedStably_WhateverThePoolHandsBack()
        {
            // The live pool is a ConcurrentDictionary (#209), so row order has to be imposed, not taken.
            ArmyListFile attacker = Army(Unit("Mixed", 6, 4, 4,
                Rifle(2, name: "Zeta Gun"), Rifle(2, name: "Alpha Gun"), Rifle(2, name: "Mu Gun")));
            ArmyListFile defender = Army(Unit("Targets", 10, 4, 4));

            for (int run = 0; run < 3; run++)
            {
                CombatReport report = CombatCalculator.Run(attacker, defender, new CombatSituation());
                Assert.That(report.Volleys.Select(volley => volley.Weapon.Name),
                    Is.EqualTo(new[] { "Alpha Gun", "Mu Gun", "Zeta Gun" }));
            }
        }

        [Test]
        public void TheSameQuestionAlwaysGetsTheSameAnswer()
        {
            ArmyListFile attacker = Army(Unit("Shooters", 10, 4, 4, Rifle(10, ap: 1)));
            ArmyListFile defender = Army(Unit("Targets", 10, 4, 4, rules: new[] { "Tough(3)" }));
            var situation = new CombatSituation(DistanceInches: 11f, DefenderInCover: true);

            CombatReport first = CombatCalculator.Run(attacker, defender, situation);
            CombatReport second = CombatCalculator.Run(attacker, defender, situation);

            Assert.Multiple(() =>
            {
                Assert.That(second.ExpectedHits, Is.EqualTo(first.ExpectedHits).Within(Tolerance));
                Assert.That(second.ExpectedWounds, Is.EqualTo(first.ExpectedWounds).Within(Tolerance));
                Assert.That(second.DefenderWoundsAfter, Is.EqualTo(first.DefenderWoundsAfter).Within(Tolerance));
            });
        }

        [Test]
        public void RunningTwiceDoesNotLetTheFirstFightBleedIntoTheSecond()
        {
            // Each run builds its own world; a defender killed in one must be untouched in the next.
            ArmyListFile attacker = Army(Unit("Shooters", 10, 2, 4, Rifle(10, attacks: 3, ap: 3)));
            ArmyListFile defender = Army(Unit("Targets", 3, 4, 6));

            CombatReport first = CombatCalculator.Run(attacker, defender, new CombatSituation());
            CombatReport second = CombatCalculator.Run(attacker, defender, new CombatSituation());

            Assert.That(second.DefenderWoundsBefore, Is.EqualTo(first.DefenderWoundsBefore).Within(Tolerance));
        }

        [Test]
        public void AUnitWithNoRangedWeapons_ReportsNothingRatherThanThrowing()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Brawlers", 5, 4, 4, Blade(5))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(DistanceInches: 12f));

            Assert.Multiple(() =>
            {
                Assert.That(report.Volleys, Is.Empty);
                Assert.That(report.ExpectedWounds, Is.EqualTo(0f).Within(Tolerance));
                Assert.That(report.Notes, Has.Some.Contains("no ranged weapons"));
            });
        }


        // ---- melee ----------------------------------------------------------------------------------

        [Test]
        public void MeleeSwingsTheMeleeWeapons_AndLeavesTheGunsAlone()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Brawlers", 5, 4, 4, Rifle(5), Blade(5))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(Mode: ECombatMode.Melee));

            Assert.Multiple(() =>
            {
                Assert.That(report.Volleys.Select(volley => volley.Weapon.Name), Is.EqualTo(new[] { "Blade" }));
                // 5 blades x 2 attacks on 4+ = 5 hits, half of them saved.
                Assert.That(report.Volleys[0].ExpectedHits, Is.EqualTo(5f).Within(Tolerance));
                Assert.That(report.ExpectedWounds, Is.EqualTo(2.5f).Within(Tolerance));
            });
        }

        [Test]
        public void AFatiguedAttackerOnlyHitsOnSixes()
        {
            CombatSituation melee = new(Mode: ECombatMode.Melee);
            ArmyListFile brawlers = Army(Unit("Brawlers", 5, 4, 4, Blade(5)));
            ArmyListFile targets = Army(Unit("Targets", 10, 4, 4));

            CombatReport fresh = CombatCalculator.Run(brawlers, targets, melee);
            CombatReport tired = CombatCalculator.Run(brawlers, targets, melee with { AttackerFatigued = true });

            Assert.Multiple(() =>
            {
                Assert.That(fresh.Volleys[0].HitRollNeeded, Is.EqualTo(4));
                Assert.That(tired.Volleys[0].HitRollNeeded, Is.EqualTo(6), "fatigue means 6s only");
                Assert.That(tired.Volleys[0].ExpectedHits, Is.EqualTo(10f / 6f).Within(Tolerance));
            });
        }

        // #418: the calculator runs the real stages, so a fatigued charge's 6s add no Furious hits there
        // either. 10 dice: fresh = 5 hits on 4+ plus 10/6 Furious extras; fatigued = only the 10/6 sixes.
        [Test]
        public void AFatiguedFuriousCharger_GetsNoExtraHitsFromItsSixes()
        {
            CombatSituation charge = new(Mode: ECombatMode.Melee, AttackerCharging: true);
            ArmyListFile brawlers = Army(Unit("Brawlers", 5, 4, 4, new[] { "Furious" }, Blade(5)));
            ArmyListFile targets = Army(Unit("Targets", 10, 4, 4));

            CombatReport fresh = CombatCalculator.Run(brawlers, targets, charge);
            CombatReport tired = CombatCalculator.Run(brawlers, targets, charge with { AttackerFatigued = true });

            Assert.Multiple(() =>
            {
                Assert.That(fresh.Volleys[0].ExpectedHits, Is.EqualTo(5f + 10f / 6f).Within(Tolerance),
                    "fresh: Furious adds one hit per natural 6");
                Assert.That(tired.Volleys[0].ExpectedHits, Is.EqualTo(10f / 6f).Within(Tolerance),
                    "fatigued: the 6s hit but don't count as 6s for Furious");
            });
        }

        [Test]
        public void AChargingImpactUnit_LandsItsHitsBeforeAnythingSwings()
        {
            CombatReport charging = CombatCalculator.Run(
                Army(Unit("Ram", 1, 4, 4, new[] { "Impact(6)" }, Blade(1))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(Mode: ECombatMode.Melee, AttackerCharging: true));

            VolleyReport impact = charging.Volleys[0];
            Assert.Multiple(() =>
            {
                Assert.That(impact.Weapon.Name, Is.EqualTo("Impact"), "impact resolves first");
                Assert.That(impact.AttackDice, Is.EqualTo(6f).Within(Tolerance));
                Assert.That(impact.ExpectedHits, Is.EqualTo(5f).Within(Tolerance), "6 dice hitting on 2+");
                Assert.That(impact.ExpectedWounds, Is.EqualTo(2.5f).Within(Tolerance), "half of them saved");
            });
        }

        // #447: Impact(X) is per model - the calculator asks with the stage's own participants and runs the real
        // stage, so a 5-model Impact(1) unit brings 5 dice, and the row says where they came from.
        [Test]
        public void AMultiModelImpactUnit_RollsOneDiePerModel()
        {
            CombatReport charging = CombatCalculator.Run(
                Army(Unit("Riders", 5, 4, 4, new[] { "Impact(1)" }, Blade(5))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(Mode: ECombatMode.Melee, AttackerCharging: true));

            VolleyReport impact = charging.Volleys[0];
            Assert.Multiple(() =>
            {
                Assert.That(impact.Weapon.Name, Is.EqualTo("Impact"));
                Assert.That(impact.AttackDice, Is.EqualTo(5f).Within(Tolerance), "5 models x Impact(1)");
                Assert.That(impact.ExpectedWounds, Is.EqualTo(5f * 5f / 6f * 0.5f).Within(Tolerance));
                Assert.That(impact.HitTags, Does.Contain("5 models x Impact(1)"));
            });
        }

        [Test]
        public void AJoinedHerosImpact_AddsItsDiceToTheHosts()
        {
            UnitFileEntry knights = Unit("Knights", 3, 4, 4, new[] { "Impact(1)" }, Blade(3));
            knights.Id = "host";
            UnitFileEntry hero = Unit("Hero", 1, 4, 4, new[] { "Hero", "Impact(3)" }, Blade(1));
            hero.JoinsUnitId = "host";

            CombatReport charging = CombatCalculator.Run(Army(knights, hero), Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(Mode: ECombatMode.Melee, AttackerCharging: true));

            VolleyReport impact = charging.Volleys[0];
            Assert.Multiple(() =>
            {
                Assert.That(impact.AttackDice, Is.EqualTo(6f).Within(Tolerance), "3 x Impact(1) + the hero's Impact(3)");
                Assert.That(impact.HitTags, Does.Contain("3 models x Impact(1) + 1 model x Impact(3)"));
            });
        }

        // #422: the calculator runs the real ResolveImpactHitsStage, so a fatigued charger's Impact rolls no
        // dice there either ("..., unless this unit is Fatigued") and no Impact row appears.
        [Test]
        public void AFatiguedImpactCharger_GetsNoImpactRow()
        {
            CombatReport tired = CombatCalculator.Run(
                Army(Unit("Ram", 1, 4, 4, new[] { "Impact(6)" }, Blade(1))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(Mode: ECombatMode.Melee, AttackerCharging: true, AttackerFatigued: true));

            Assert.That(tired.Volleys.Select(volley => volley.Weapon.Name), Is.EqualTo(new[] { "Blade" }));
        }

        [Test]
        public void WithoutACharge_ThereIsNoImpactRow()
        {
            ArmyListFile ram = Army(Unit("Ram", 1, 4, 4, new[] { "Impact(6)" }, Blade(1)));
            ArmyListFile targets = Army(Unit("Targets", 10, 4, 4));

            CombatReport standing = CombatCalculator.Run(ram, targets,
                new CombatSituation(Mode: ECombatMode.Melee, AttackerCharging: false));

            Assert.That(standing.Volleys.Any(volley => volley.Weapon.Name == "Impact"), Is.False);
        }

        [Test]
        public void AChargerWithNoImpactRule_GetsNoImpactRowEither()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Brawlers", 5, 4, 4, Blade(5))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(Mode: ECombatMode.Melee, AttackerCharging: true));

            Assert.That(report.Volleys.Select(volley => volley.Weapon.Name), Is.EqualTo(new[] { "Blade" }));
        }

        [Test]
        public void MeleeSaysWhatItIsAssuming()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Brawlers", 5, 4, 4, Blade(5))),
                Army(Unit("Targets", 10, 4, 4)),
                new CombatSituation(Mode: ECombatMode.Melee));

            Assert.That(report.Notes, Has.Some.Contains("Strike-back"),
                "the missing half of a melee must be stated, not silently omitted");
        }

        // #442 (#432): the charge's launch distance is an input now, and it reaches the real stages - so a Ferocious
        // charger's extra hit on each natural 6 appears charging from 7" and not from 5" (or when no distance is given).
        [Test]
        public void ChargeFrom_ReachesTheChargeArms()
        {
            ArmyListFile chargers = Army(Unit("Raiders", 5, 4, 4, new[] { "Ferocious" }, Blade(5, attacks: 3)));
            chargers.RuleDefinitions.Add(RangeConditional36Definitions.ExtraHit("Ferocious"));
            ArmyListFile targets = Army(Unit("Targets", 20, 4, 6));

            float Wounds(float from) => CombatCalculator.Run(chargers, targets,
                new CombatSituation(ECombatMode.Melee, AttackerCharging: true, ChargeFromInches: from)).ExpectedWounds;

            // 15 attacks at 4+ = 7.5 hits, +1/6 per die (2.5) from over 6"; Defense 6 lets 5/6 through.
            Assert.That(Wounds(7f), Is.EqualTo(10f * 5f / 6f).Within(Tolerance));
            Assert.That(Wounds(5f), Is.EqualTo(7.5f * 5f / 6f).Within(Tolerance));
            Assert.That(Wounds(0f), Is.EqualTo(Wounds(5f)).Within(Tolerance), "no distance given: no charge arm");
        }

        // #442 (#434): Ravage's pre-strike dice run through the real ResolveRavageWoundsStage and get their own row -
        // unsaveable wounds, before the swings.
        [Test]
        public void ARavageUnit_GetsAPreStrikeRow_WithNoSave()
        {
            CombatReport report = CombatCalculator.Run(
                Army(Unit("Brutes", 2, 4, 4, new[] { "Ravage(3)" }, Blade(2))),
                Army(Unit("Knights", 10, 4, 2)),
                new CombatSituation(ECombatMode.Melee, AttackerCharging: true));

            VolleyReport ravage = report.Volleys.Single(v => v.Weapon.Name == "Ravage");
            Assert.Multiple(() =>
            {
                Assert.That(ravage.AttackDice, Is.EqualTo(6f).Within(Tolerance), "2 models x Ravage(3)");
                Assert.That(ravage.ExpectedWounds, Is.EqualTo(1f).Within(Tolerance), "6 dice at 6+, Defense 2 never rolled");
                Assert.That(report.Volleys.ToList().IndexOf(ravage), Is.LessThan(report.Volleys.Count - 1), "before the swings");
            });
        }

        // ---- fixture helpers ---------------------------------------------------------------------

        private const float Tolerance = 0.001f;

        private static ArmyListFile Army(params UnitFileEntry[] units) => new ArmyListFile
        {
            Name = "Sandbox",
            Faction = "Test",
            Units = new List<UnitFileEntry>(units),
        };

        private static UnitFileEntry Unit(string name, int models, int quality, int defense,
            params WeaponFileEntry[] weapons)
            => Unit(name, models, quality, defense, null, weapons);

        private static UnitFileEntry Unit(string name, int models, int quality, int defense,
            string[]? rules, params WeaponFileEntry[] weapons) => new UnitFileEntry
            {
                Name = name,
                ModelCount = models,
                Quality = quality,
                Defense = defense,
                PointCost = 100,
                Weapons = new List<WeaponFileEntry>(weapons),
                SpecialRules = (rules ?? System.Array.Empty<string>()).Select(ToEntry).ToList(),
            };

        // "Tough(3)" -> the numeric entry the army file would carry; a bare name -> the plain one.
        private static SpecialRuleEntry ToEntry(string rule)
        {
            int open = rule.IndexOf('(');
            if (open < 0)
            {
                return new SpecialRuleEntry_Core(rule);
            }

            string name = rule[..open];
            int value = int.Parse(rule[(open + 1)..rule.IndexOf(')')]);
            return new SpecialRuleEntry_CoreNumeric(name, value);
        }

        // Quantity is the unit's TOTAL copies (they are dealt round-robin across models), so a weapon
        // for every model in a 10-model unit is Quantity 10.
        private static WeaponFileEntry Rifle(int quantity, int range = 24, int attacks = 1, int ap = 0,
            string name = "Rifle") => new WeaponFileEntry
            {
                Name = name,
                Quantity = quantity,
                RangeInches = range,
                Attacks = attacks,
                ArmorPenetration = ap,
            };

        private static WeaponFileEntry Blade(int quantity, int attacks = 2, int ap = 0)
            => Rifle(quantity, range: 0, attacks: attacks, ap: ap, name: "Blade");
    }
}
