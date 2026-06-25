using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Optional force-organization check (#003). Computes army-composition warnings against the GDF
    /// caps (cost / hero / copy / unit) but is purely advisory — it never blocks saving or launching.
    /// Pure and side-effect-free so it can be unit-tested and reused (e.g. by the lobby) without any UI.
    /// </summary>
    public static class ForceOrgValidator
    {
        /// <summary>One Hero is allowed per this many points of the army's limit (1 per 500).</summary>
        public const int POINTS_PER_HERO = 500;

        /// <summary>Maximum copies of any single (same-named) unit.</summary>
        public const int MAX_COPIES_PER_UNIT = 3;

        /// <summary>
        /// Returns one human-readable warning for each force-org cap the army exceeds, in display order.
        /// An empty list means the army is within every cap. Never throws and never blocks.
        /// </summary>
        public static IReadOnlyList<string> Validate(ArmyListFile army)
        {
            List<string> warnings = new List<string>();

            // Cost cap — total points must not exceed the army's limit.
            if (army.TotalPoints > army.PointsLimit)
            {
                warnings.Add($"Over points limit: {army.TotalPoints} / {army.PointsLimit} pts.");
            }

            // Hero cap — 1 Hero per 500 pts of the limit.
            int heroCount = army.Units.Count(IsHero);
            int heroAllowance = army.PointsLimit / POINTS_PER_HERO;
            if (heroCount > heroAllowance)
            {
                warnings.Add($"Too many Heroes: {heroCount} (max {heroAllowance} at {army.PointsLimit} pts; 1 per {POINTS_PER_HERO}).");
            }

            // Copy cap — no more than 3 of any same-named unit. Blank names are skipped, since several
            // half-built (unnamed) entries shouldn't read as duplicates of each other.
            foreach (IGrouping<string, UnitFileEntry> group in army.Units
                .Where(u => !string.IsNullOrWhiteSpace(u.Name))
                .GroupBy(u => u.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > MAX_COPIES_PER_UNIT))
            {
                warnings.Add($"Too many copies of \"{group.Key}\": {group.Count()} (max {MAX_COPIES_PER_UNIT}).");
            }

            // Unit cap — an army of only Heroes has no rank-and-file unit for them to lead. (An entirely
            // empty army is left alone — it's just unfinished, not mis-composed.)
            if (army.Units.Count > 0 && army.Units.All(IsHero))
            {
                warnings.Add("Army has no non-Hero units.");
            }

            return warnings;
        }

        /// <summary>True if the unit carries the Hero rule (recursing through alias entries). The single
        /// definition of "is this army-file unit a Hero", shared with the Army Builder UI.</summary>
        public static bool IsHero(UnitFileEntry unit) => unit.SpecialRules.Any(EntryIsHero);

        private static bool EntryIsHero(SpecialRuleEntry entry) => entry switch
        {
            SpecialRuleEntry_Core core => string.Equals(core.Name, "Hero", StringComparison.Ordinal),
            SpecialRuleEntry_CoreNumeric num => string.Equals(num.Name, "Hero", StringComparison.Ordinal),
            SpecialRuleEntry_Alias alias => EntryIsHero(alias.AliasedRule),
            _ => false,
        };
    }
}
