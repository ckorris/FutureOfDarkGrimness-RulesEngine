using System;

namespace FDG.SaveLoad
{
    /// <summary>
    /// #444 D2 (Chris, 2026-10-31: option B) - a saved game written under an older OPR ruleset still loads and plays,
    /// under the CURRENT rules; what the player is owed is a one-line, non-blocking heads-up, the same shape as the
    /// #440 <see cref="ArmyBuilding.LegacyArmyNotice"/> for armies (amber in the lobby, a plain line on the CLI).
    ///
    /// The ruleset a save was written under is its <see cref="GameSaveFile.RulesVersion"/> stamp, which
    /// <see cref="GameSaveSerializer.Save"/> has written since the 3.6 upgrade. Unlike an army, where a missing
    /// stamp means a hand-authored list that nothing says is old, a save with NO stamp is legacy by definition:
    /// every save written before this change has none ("the absence of a version is an indicator that it's too old").
    /// </summary>
    public static class LegacySaveNotice
    {
        /// <summary>True when <paramref name="rulesVersion"/> is absent, unparseable, or names a ruleset other than
        /// <see cref="OprRulesVersion.CurrentMajorMinor"/>.</summary>
        public static bool IsLegacy(string? rulesVersion) =>
            !string.Equals(OprRulesVersion.MajorMinorOf(rulesVersion), OprRulesVersion.CurrentMajorMinor,
                StringComparison.Ordinal);

        /// <summary>The notice for a save loaded with <paramref name="rulesVersion"/> (the out value of
        /// <see cref="GameSaveSerializer.Load(string, out string?)"/>), or null when the save is current.
        /// ASCII only (it is game text).</summary>
        public static string? Line(string? rulesVersion)
        {
            if (!IsLegacy(rulesVersion)) return null;
            string current = OprRulesVersion.CurrentMajorMinor;
            return string.IsNullOrWhiteSpace(rulesVersion)
                ? $"This save predates the OPR {current} upgrade; it plays with {current} rules"
                : $"This save was written for {rulesVersion.Trim()}; it plays with {current} rules";
        }
    }
}
