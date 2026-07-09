using FDG.SaveLoad;

namespace FDG.Tests
{
    /// <summary>
    /// Army fixtures shared by the whole-game test fixtures (DeterminismTests, TacticianScaffoldTests).
    /// </summary>
    internal static class TestArmies
    {
        public static ArmyListFile MakeShooterArmy() => new ArmyListFile
        {
            Name = "Shooters",
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Warriors", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
            },
        };

        public static ArmyListFile MakeDefenderArmy() => new ArmyListFile
        {
            Name = "Defenders",
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Guards", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
            },
        };

        // The FdgLab builtin army's rule spread: movement rules (Very Fast/Vanguard/Scout), deferred
        // deployment (Ambush), melee reactions (Counter/Thrust/Impact/Furious), weapon rules
        // (Surge/Blast/Takedown), Strafing, Martial Prowess - the paths the simple armies never touch.
        public static ArmyListFile MakeRichArmy(string name) => new ArmyListFile
        {
            Name = name,
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Warriors", ModelCount = 5, Quality = 4, Defense = 4,
                    SpecialRules = new()
                    {
                        new SpecialRuleEntry_Core("Stealth"),
                        new SpecialRuleEntry_Core("Very Fast"),
                        new SpecialRuleEntry_Core("Vanguard"),
                        new SpecialRuleEntry_Core("Thrust"),
                        new SpecialRuleEntry_CoreNumeric("Impact", 2),
                        new SpecialRuleEntry_Core("Furious"),
                        new SpecialRuleEntry_Core("Strafing"),
                    },
                    Weapons = new()
                    {
                        new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 },
                        new WeaponFileEntry { Name = "Blade", RangeInches = 0, Attacks = 2 },
                    },
                },
                new UnitFileEntry
                {
                    Name = "Heavy Gunners", ModelCount = 3, Quality = 4, Defense = 4,
                    SpecialRules = new()
                    {
                        new SpecialRuleEntry_Core("Scout"),
                        new SpecialRuleEntry_Core("Martial Prowess"),
                    },
                    Weapons = new()
                    {
                        new WeaponFileEntry
                        {
                            Name = "Heavy Rifle", RangeInches = 36, Attacks = 1,
                            SpecialRules = new()
                            {
                                new SpecialRuleEntry_Core("Surge"),
                                new SpecialRuleEntry_CoreNumeric("Blast", 3),
                            },
                        },
                        new WeaponFileEntry
                        {
                            Name = "Fists", RangeInches = 0, Attacks = 1,
                            SpecialRules = new() { new SpecialRuleEntry_Core("Counter") },
                        },
                    },
                },
                new UnitFileEntry
                {
                    Name = "Infiltrators", ModelCount = 2, Quality = 4, Defense = 4,
                    SpecialRules = new() { new SpecialRuleEntry_Core("Ambush") },
                    Weapons = new()
                    {
                        new WeaponFileEntry
                        {
                            Name = "Rifle", RangeInches = 24, Attacks = 1,
                            SpecialRules = new() { new SpecialRuleEntry_Core("Takedown") },
                        },
                    },
                },
            },
        };
    }
}
