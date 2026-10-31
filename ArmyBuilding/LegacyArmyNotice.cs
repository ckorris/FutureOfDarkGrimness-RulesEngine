using System;
using System.Text.RegularExpressions;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    /// <summary>
    /// #440 (plan Q5) - the one backward-compatibility promise of the OPR 3.6 upgrade is that an army saved under an
    /// older ruleset still loads and plays: its embedded book definitions keep working and anything the current
    /// catalog dropped degrades through the #168 diagnostic. What the player is owed is a heads-up that the list
    /// plays under the CURRENT rules - a one-line, non-blocking notice wherever an army is fielded (the lobby roster,
    /// the CLI army prompt).
    ///
    /// The ruleset an army was built for is the version stamp of the book embedded in a Forge-built army
    /// (<see cref="BuiltArmyFile.Book"/>, "OPR v3.5.3"). A hand-authored list carries no book and so no stamp, and
    /// gets no notice: there is nothing to say it is old.
    /// </summary>
    public static class LegacyArmyNotice
    {
        /// <summary>The OPR ruleset this build plays (major.minor).</summary>
        public const string CurrentRules = "3.6";

        private static readonly Regex VersionPattern = new(@"v?(\d+)\.(\d+)", RegexOptions.CultureInvariant);

        /// <summary>The ruleset stamp of the book embedded in <paramref name="army"/>, or null when it embeds
        /// none (a hand-authored list, or a file read as the base type).</summary>
        public static string? RulesVersionOf(ArmyListFile? army) =>
            army is BuiltArmyFile { Book: { } book } && !string.IsNullOrWhiteSpace(book.Version)
                ? book.Version.Trim()
                : null;

        /// <summary>True when <paramref name="rulesVersion"/> names a ruleset other than
        /// <see cref="CurrentRules"/>. Null, empty or unparseable stamps are not legacy - nothing says so.</summary>
        public static bool IsLegacy(string? rulesVersion)
        {
            if (string.IsNullOrWhiteSpace(rulesVersion)) return false;
            Match m = VersionPattern.Match(rulesVersion);
            return m.Success && !string.Equals($"{m.Groups[1].Value}.{m.Groups[2].Value}", CurrentRules,
                StringComparison.Ordinal);
        }

        /// <summary>The notice for one army, or null when it is not legacy. ASCII only (it is game text).</summary>
        public static string? Line(string? armyName, string? rulesVersion) =>
            IsLegacy(rulesVersion)
                ? $"Army '{armyName}' was built for {rulesVersion}; it plays with {CurrentRules} rules"
                : null;

        /// <summary><see cref="Line(string?, string?)"/> for a loaded army file.</summary>
        public static string? Line(ArmyListFile? army) => army is null ? null : Line(army.Name, RulesVersionOf(army));
    }
}
