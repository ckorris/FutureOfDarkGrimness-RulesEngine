using System;
using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #241 v2 — rebuild an EDITABLE Forge session (BuilderList) from a share list, against the bundled
    // book: the "Open in Forge" path. Unlike Import (verbatim Army Forge data), a reconstructed list
    // compiles through OUR ListCompiler — which is the point: comparing our per-unit costs against Army
    // Forge's authoritative ones turns every import into a pricing reconciliation (#218/#219 get real
    // repro cases for free). A unit the bundled book doesn't know is EXCLUDED and reported with its cost
    // so the user can pad the list in the Forge; an unmatched upgrade choice drops with a warning while
    // its unit stays — the points delta discloses the gap either way.
    public static partial class OprListImporter
    {
        public static OprForgeSessionResult ReconstructSelections(string listJson, BookFile book)
        {
            var warnings = new List<string>();
            OprList list = Parse(OprBookImporter.AsciiFoldJsonValues(listJson, warnings.Add), warnings.Add);

            var selections = new BuilderList
            {
                Name = string.IsNullOrWhiteSpace(list.Name) ? "Imported Army" : list.Name!,
                BookName = book.Name,
                PointsLimit = list.PointsLimit ?? 0,
            };

            var included = new List<OprListUnit>();
            var excluded = new List<(string Name, int Points)>();

            foreach (OprListUnit dto in list.Units ?? new())
            {
                string name = DisplayName(dto);
                RosterUnit? roster = book.Units.FirstOrDefault(u => u.Id == dto.Id);
                if (roster is null)
                {
                    excluded.Add((name, dto.Cost));
                    warnings.Add($"'{name}' is not in the bundled '{book.Name}' book - excluded ({dto.Cost} pts). " +
                        "Add units in the Forge to replace it.");
                    continue;
                }

                var bu = new BuilderUnit
                {
                    RosterUnitId = roster.Id,
                    ModelCount = roster.BaseModelCount,
                    Id = dto.SelectionId,
                    // joinToUnit is overloaded upstream: on a combined half it names the partner copy, on a
                    // hero the unit it joins — the same split #107/#006 model the builder already edits.
                    JoinsUnitId = dto.Combined ? null : dto.JoinToUnit,
                    CombinedWithId = dto.Combined ? dto.JoinToUnit : null,
                };
                ReconstructChoices(dto, roster, bu, name, warnings);
                selections.Units.Add(bu);
                included.Add(dto);
            }

            // Mirror the builder's RemoveFromList hygiene: links into an excluded unit must not dangle.
            var ids = new HashSet<string>(selections.Units.Select(u => u.Id).OfType<string>());
            foreach (BuilderUnit bu in selections.Units)
            {
                if (bu.JoinsUnitId is not null && !ids.Contains(bu.JoinsUnitId))
                {
                    warnings.Add("A hero's join target was excluded - the hero will deploy on its own.");
                    bu.JoinsUnitId = null;
                }
                if (bu.CombinedWithId is not null && !ids.Contains(bu.CombinedWithId))
                {
                    warnings.Add("A combined unit's partner copy was excluded - imported as a separate unit.");
                    bu.CombinedWithId = null;
                }
            }

            var result = new OprForgeSessionResult { Selections = selections };
            result.Warnings.AddRange(warnings);
            result.ExcludedUnits.AddRange(excluded);

            // The reconciliation an import doubles as: our compiler's prices vs Army Forge's. Row-aligned
            // (CompileRows keeps combined halves as separate rows, exactly like the TTS entries).
            List<UnitFileEntry> rows = ListCompiler.CompileRows(book, selections);
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].PointCost != included[i].Cost)
                    result.UnitPointsDeltas.Add((DisplayName(included[i]), rows[i].PointCost, included[i].Cost));
            }
            result.OurTotalPoints = ListCompiler.Compile(book, selections).TotalPoints;
            result.TheirTotalPoints = included.Sum(d => d.Cost);
            return result;
        }

        private static string DisplayName(OprListUnit dto) =>
            string.IsNullOrWhiteSpace(dto.CustomName) ? (dto.Name ?? "Unit") : dto.CustomName!;

        // Defensive ladder over the least-verified corpus shape (selectedUpgrades, #241): the section by
        // the upgrade's id/uid, else the unique section owning the option id, else a unique option-label
        // match. Anything unmatched drops with a warning — the points reconciliation shows the resulting
        // gap regardless, so a silent mismatch can't hide.
        private static void ReconstructChoices(OprListUnit dto, RosterUnit roster, BuilderUnit bu,
            string name, List<string> warnings)
        {
            foreach (OprSelectedUpgrade selected in dto.SelectedUpgrades ?? new())
            {
                string? sectionId = selected.Upgrade?.Uid ?? selected.Upgrade?.Id ?? selected.Upgrade?.SectionId;
                string? optionId = selected.Option?.Uid ?? selected.Option?.Id;

                UpgradeSection? section = sectionId is null ? null
                    : roster.Sections.FirstOrDefault(s => s.Id == sectionId);
                UpgradeOption? option = optionId is null ? null
                    : section?.Options.FirstOrDefault(o => o.Id == optionId);

                if (option is null && optionId is not null)
                    (section, option) = FindUniqueOption(roster, o => o.Id == optionId);
                if (option is null && selected.Option?.Label is { Length: > 0 } label)
                    (section, option) = FindUniqueOption(roster,
                        o => string.Equals(o.Label, label, StringComparison.OrdinalIgnoreCase));

                if (section is null || option is null)
                {
                    warnings.Add($"'{name}': upgrade '{selected.Option?.Label ?? optionId ?? "?"}' could not be " +
                        "matched in the bundled book - dropped (points will differ).");
                    continue;
                }

                UpgradeChoice? existing = bu.Choices.FirstOrDefault(c =>
                    c.SectionId == section.Id && c.OptionId == option.Id);
                if (existing is not null) existing.Count++;
                else bu.Choices.Add(new UpgradeChoice { SectionId = section.Id, OptionId = option.Id, Count = 1 });
            }
        }

        private static (UpgradeSection?, UpgradeOption?) FindUniqueOption(RosterUnit roster,
            Func<UpgradeOption, bool> match)
        {
            var owners = roster.Sections
                .Select(s => (Section: s, Option: s.Options.FirstOrDefault(match)))
                .Where(x => x.Option is not null)
                .ToList();
            return owners.Count == 1 ? (owners[0].Section, owners[0].Option) : (null, null);
        }
    }

    /// <summary>What <see cref="OprListImporter.ReconstructSelections"/> produced: an editable Forge
    /// session plus the reconciliation findings. <see cref="UnitPointsDeltas"/> holds every unit where our
    /// ListCompiler's price disagrees with Army Forge's (a #218/#219-class bug surfaced by real data);
    /// <see cref="ExcludedUnits"/> are list units the bundled book doesn't contain, reported with their
    /// Army Forge cost so the user knows how many points to pad.</summary>
    public sealed class OprForgeSessionResult
    {
        public required BuilderList Selections { get; init; }
        public List<string> Warnings { get; } = new();
        public List<(string Name, int Points)> ExcludedUnits { get; } = new();
        public List<(string Name, int OurPoints, int TheirPoints)> UnitPointsDeltas { get; } = new();
        public int OurTotalPoints { get; set; }
        public int TheirTotalPoints { get; set; }
    }
}
