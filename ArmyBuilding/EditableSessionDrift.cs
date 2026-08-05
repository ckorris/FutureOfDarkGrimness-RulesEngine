using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #356 — a .fdgarmy can carry TWO derivations of the same list, and they do not have to agree.
    //
    // The Forge's own Save writes both halves from one source: ArmyListFile.Units is exactly what
    // ListCompiler produced from the embedded Selections, so the two always match and reopening is lossless.
    //
    // An Army Forge import saved via "Save As" (#241) is different: the playable half is Army Forge's
    // VERBATIM data (their units, their authoritative points), while the editable half is our
    // reconstruction against the bundled book. Those diverge in the three ways the import preview already
    // discloses - units the bundled book does not know, upgrade choices that did not match, and per-unit
    // pricing (#218/#219). Reopening such a file for editing therefore recompiles it into something that
    // may differ from the army as saved and played.
    //
    // Measuring that gap is army-data knowledge, so it lives here rather than in the front end: the caller
    // asks before adopting, and shows the user what reopening would change.

    /// <summary>How far a file's editable session (<see cref="BuiltArmyFile.Selections"/> compiled against
    /// <see cref="BuiltArmyFile.Book"/>) has drifted from the playable army saved alongside it.</summary>
    public sealed class EditableSessionDrift
    {
        public required int SavedUnitCount { get; init; }
        public required int RebuiltUnitCount { get; init; }
        public required int SavedPoints { get; init; }
        public required int RebuiltPoints { get; init; }

        /// <summary>Units present in the saved army that the rebuilt session does not produce - by name,
        /// counting duplicates, so two copies lost out of three are reported as one.</summary>
        public required IReadOnlyList<string> DroppedUnits { get; init; }

        /// <summary>True when reopening for editing would change the army. False means the rebuild
        /// reproduces the saved army and can be adopted silently.</summary>
        public bool Differs =>
            SavedUnitCount != RebuiltUnitCount || SavedPoints != RebuiltPoints || DroppedUnits.Count > 0;
    }

    public static class EditableSession
    {
        private const string CombinedSuffix = "(Combined)";

        /// <summary>A unit name with the #107 combined marker removed, so the same unit compares equal
        /// whether it was saved as the merged pair or rebuilt from its two roster copies.</summary>
        public static string NormalizeUnitName(string name) =>
            name.EndsWith(CombinedSuffix, StringComparison.Ordinal)
                ? name[..^CombinedSuffix.Length].TrimEnd()
                : name;

        /// <summary>Build a file that PLAYS as <paramref name="playable"/> but REOPENS as the given editable
        /// session. The playable half is copied through a serialization round-trip rather than field by
        /// field, so a future <see cref="ArmyListFile"/> field cannot be silently dropped here - the same
        /// round-trip the file already survives on save/load.</summary>
        public static BuiltArmyFile Attach(ArmyListFile playable, BuilderList selections, BookFile book)
        {
            if (playable is null) throw new ArgumentNullException(nameof(playable));
            if (selections is null) throw new ArgumentNullException(nameof(selections));
            if (book is null) throw new ArgumentNullException(nameof(book));

            string json = JsonSerializer.Serialize(playable, playable.GetType(), RuleJson.Options);
            BuiltArmyFile file = JsonSerializer.Deserialize<BuiltArmyFile>(json, RuleJson.Options)
                ?? throw new InvalidOperationException("Army round-trip produced no file.");

            file.Selections = selections;
            file.Book = book;
            return file;
        }

        /// <summary>Measure what reopening <paramref name="file"/> for editing would change. Null when the
        /// file carries no editable session at all (nothing to reopen, nothing to compare).</summary>
        public static EditableSessionDrift? Measure(BuiltArmyFile file)
        {
            if (file?.Selections is null || file.Book is null) return null;

            BuiltArmyFile rebuilt = ListCompiler.Compile(file.Book, file.Selections);

            // Multiset difference by name: every saved unit the rebuild does not account for. Names are
            // normalized first - a #107 pair can be saved as "Warriors (Combined)" while the rebuild merges
            // two copies and calls the result "Warriors", which is the same unit, not a loss.
            var remaining = rebuilt.Units.Select(u => NormalizeUnitName(u.Name)).ToList();
            var dropped = new List<string>();
            foreach (UnitFileEntry saved in file.Units)
            {
                int at = remaining.IndexOf(NormalizeUnitName(saved.Name));
                if (at >= 0) remaining.RemoveAt(at);
                else dropped.Add(saved.Name);
            }

            return new EditableSessionDrift
            {
                SavedUnitCount = file.Units.Count,
                RebuiltUnitCount = rebuilt.Units.Count,
                SavedPoints = file.TotalPoints,
                RebuiltPoints = rebuilt.TotalPoints,
                DroppedUnits = dropped,
            };
        }
    }
}
