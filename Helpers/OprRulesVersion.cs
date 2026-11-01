using System;
using System.Text.RegularExpressions;

namespace FDG
{
    /// <summary>
    /// The OPR ruleset this build implements, as the ONE engine constant everything else derives from (#444 D2):
    /// the stamp written into every saved game (<see cref="SaveLoad.GameSaveSerializer.Save"/>), and the
    /// major.minor the legacy notices compare an old file's stamp against (<see cref="ArmyBuilding.LegacyArmyNotice"/>
    /// for armies, <see cref="SaveLoad.LegacySaveNotice"/> for saves). Same shape as the version stamp of the shipped
    /// books ("OPR v3.6.0", <see cref="ArmyBuilding.BookFile.Version"/>), so a book stamp and a save stamp compare alike.
    ///
    /// Bump <see cref="CurrentStamp"/> with the ruleset, alongside RULES_VERSION in the superproject's
    /// scripts/build-dist.sh and .github/workflows/release.yml (CLAUDE.md, Versioning): the release title, the
    /// archive names and the stamp inside every save must all name the same ruleset.
    /// </summary>
    public static class OprRulesVersion
    {
        /// <summary>The full stamp of the ruleset this build plays, "OPR v3.6.0".</summary>
        public const string CurrentStamp = "OPR v3.6.0";

        // Declared before CurrentMajorMinor: static field initializers run in textual order.
        private static readonly Regex VersionPattern = new(@"v?(\d+)\.(\d+)", RegexOptions.CultureInvariant);

        /// <summary>The ruleset this build plays as major.minor ("3.6"), derived from <see cref="CurrentStamp"/> -
        /// the level at which an older file counts as legacy (a patch bump never changes how a list or save plays).</summary>
        public static readonly string CurrentMajorMinor = MajorMinorOf(CurrentStamp)
            ?? throw new InvalidOperationException($"OprRulesVersion.CurrentStamp '{CurrentStamp}' has no major.minor.");

        /// <summary>The major.minor ("3.5") named by a stamp such as "OPR v3.5.3" or "v3.5", or null when
        /// <paramref name="stamp"/> is null, blank or names no version at all.</summary>
        public static string? MajorMinorOf(string? stamp)
        {
            if (string.IsNullOrWhiteSpace(stamp)) return null;
            Match m = VersionPattern.Match(stamp);
            return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : null;
        }
    }
}
