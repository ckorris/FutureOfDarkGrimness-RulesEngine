using System;

namespace FDG.ArmyBuilding
{
    // #378 — the OPR game systems this game's data can belong to, as Army Forge's slugs. A book or army
    // with NO GameSystem field is Grimdark Future: GDF was the only system that existed before the field
    // did, so absent-means-GDF keeps every pre-#378 file's meaning unchanged (owner ruling, 2026-08-23).
    public static class GameSystems
    {
        public const string GrimdarkFuture = "grimdark-future";
        public const string AgeOfFantasy = "age-of-fantasy";

        /// <summary>The slug a null/empty system field means: Grimdark Future.</summary>
        public static string Normalize(string? slug) =>
            string.IsNullOrWhiteSpace(slug) ? GrimdarkFuture : slug.Trim();

        /// <summary>Whether two system fields name the same game system, absent meaning GDF on both sides.</summary>
        public static bool SameSystem(string? a, string? b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }
}
