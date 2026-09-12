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

        /// <summary>The human-readable name of a game-system slug, for UI labels and gate messages.
        /// An unrecognized slug (an OPR custom-tool army, say) reads as itself rather than as a
        /// guess.</summary>
        public static string DisplayName(string? slug) => Normalize(slug) switch
        {
            GrimdarkFuture => "Grimdark Future",
            AgeOfFantasy => "Age of Fantasy",
            var other => other,
        };

        /// <summary>Whether an army whose system field is <paramref name="slug"/> may be brought to a
        /// lobby set to <paramref name="allowed"/>. <see cref="EAllowedGameSystems.All"/> takes
        /// everything, INCLUDING a slug neither constant here names - that is the whole point of the
        /// value (#400).</summary>
        public static bool IsAllowed(EAllowedGameSystems allowed, string? slug) => allowed switch
        {
            EAllowedGameSystems.GrimdarkFuture => SameSystem(slug, GrimdarkFuture),
            EAllowedGameSystems.AgeOfFantasy => SameSystem(slug, AgeOfFantasy),
            _ => true,
        };

        /// <summary>The lobby label for an <see cref="EAllowedGameSystems"/> setting.</summary>
        public static string DisplayName(EAllowedGameSystems allowed) => allowed switch
        {
            EAllowedGameSystems.GrimdarkFuture => "Grimdark Future",
            EAllowedGameSystems.AgeOfFantasy => "Age of Fantasy",
            _ => "All",
        };
    }

    /// <summary>
    /// #400 - which game systems' armies a lobby accepts, as a host-owned <see cref="GameSettings"/>
    /// setting. <see cref="All"/> is 0 so a save or config written before the field existed
    /// deserializes to it, i.e. to the pre-#400 behavior of accepting everything.
    ///
    /// <para>Deliberately NOT a flags enum over the two known slugs: OPR's custom-book tool produces
    /// armies that belong to neither collection, and <see cref="All"/> has to keep taking those. A
    /// third system, if one is ever imported, is a new member here.</para>
    /// </summary>
    public enum EAllowedGameSystems
    {
        /// <summary>Any army, whatever its system field says - including one this build doesn't
        /// recognize. The default, and what every pre-#400 file means.</summary>
        All = 0,

        /// <summary>Grimdark Future only. An army with NO system field counts as GDF
        /// (<see cref="GameSystems.Normalize"/>), so every pre-#378 army list passes.</summary>
        GrimdarkFuture = 1,

        /// <summary>Age of Fantasy only.</summary>
        AgeOfFantasy = 2,
    }
}
