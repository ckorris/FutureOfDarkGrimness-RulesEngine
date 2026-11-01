using System;
using System.Collections.Generic;

namespace FDG.ArmyBuilding
{
    /// <summary>
    /// #437 - the ONE place that knows OPR renamed army books between releases. Book names are matched as plain
    /// strings in several places (effect-set tables, rule-name fixes, the share-link bundled-book match, the
    /// legacy-list rulebook backfill, the refresh tools), so a rename would otherwise silently cost a book its
    /// curation. Every such lookup compares <see cref="Current"/> names instead of raw ones, and the curated tables
    /// are keyed by the CURRENT name only.
    ///
    /// OPR 3.6 (2026-10-29) renames, from the 3.5.3 vs 3.6.0 snapshots (Army Forge uids unchanged across each):
    /// Grimdark Future "Wormhole Daemons of X" -> "Daemons of X" and "Saurian Starhost" -> "Saurian Starhosts";
    /// Age of Fantasy "Rift Daemons of X" -> "Daemons of X". The renames are per game system: both systems now have a
    /// "Daemons of Change", so the system is part of every comparison (absent = Grimdark Future, as everywhere).
    ///
    /// Why names and not the Army Forge uid: a saved army (and the legacy-list backfill that serves it) carries only
    /// its Faction string, and the bundled books carry no uid, so a uid key would still need this table for every
    /// pre-3.6 file. The table is small, explicit, and costs nothing once nothing old is left.
    /// </summary>
    public static class OprBookNames
    {
        private static readonly Dictionary<string, string> GrimdarkRenames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Wormhole Daemons of Change"] = "Daemons of Change",
            ["Wormhole Daemons of Lust"] = "Daemons of Lust",
            ["Wormhole Daemons of Plague"] = "Daemons of Plague",
            ["Wormhole Daemons of War"] = "Daemons of War",
            ["Saurian Starhost"] = "Saurian Starhosts",
        };

        private static readonly Dictionary<string, string> FantasyRenames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Rift Daemons of Change"] = "Daemons of Change",
            ["Rift Daemons of Lust"] = "Daemons of Lust",
            ["Rift Daemons of Plague"] = "Daemons of Plague",
            ["Rift Daemons of War"] = "Daemons of War",
        };

        /// <summary>The book's current OPR name: a pre-rename name maps to its successor in the same game system;
        /// anything else (including a name that is already current, or a hand-authored faction) comes back
        /// unchanged, trimmed. Null reads as empty.</summary>
        public static string Current(string? name, string? gameSystem)
        {
            string trimmed = (name ?? string.Empty).Trim();
            var renames = string.Equals(GameSystems.Normalize(gameSystem), GameSystems.AgeOfFantasy, StringComparison.OrdinalIgnoreCase)
                ? FantasyRenames
                : GrimdarkRenames;
            return renames.TryGetValue(trimmed, out string? current) ? current : trimmed;
        }

        /// <summary>True when the two (name, system) pairs denote the same OPR book across renames: same game
        /// system, and equal <see cref="Current"/> names (case-insensitive). Empty names never match.</summary>
        public static bool SameBook(string? nameA, string? systemA, string? nameB, string? systemB)
        {
            if (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB)) return false;
            return GameSystems.SameSystem(systemA, systemB)
                && string.Equals(Current(nameA, systemA), Current(nameB, systemB), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when <paramref name="book"/> is the book a list's faction string names: its Faction or its
        /// Name (a compiled army copies the book's Faction, a hand-authored list may name the book) is the
        /// <see cref="SameBook"/> as <paramref name="faction"/> within <paramref name="gameSystem"/>.</summary>
        public static bool Matches(BookFile book, string? faction, string? gameSystem) =>
            SameBook(book.Faction, book.GameSystem, faction, gameSystem)
            || SameBook(book.Name, book.GameSystem, faction, gameSystem);
    }
}
