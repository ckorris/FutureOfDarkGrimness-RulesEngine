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
            var includedRosters = new List<RosterUnit>();
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
                includedRosters.Add(roster);
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

            // The reconciliation an import doubles as. BASE vs BASE only (corrected 2026-07-19): a share
            // list's per-unit `cost` is the unit's base cost, so comparing it against our COMPILED row cost
            // flagged every upgraded unit as a phantom delta - the upgrade points were only ever on one side
            // of that subtraction. Base-vs-base is a true like-for-like check and still catches the thing
            // this was for: bundled book data drifting from OPR's catalog.
            for (int i = 0; i < included.Count; i++)
            {
                if (includedRosters[i].BasePointCost != included[i].Cost)
                {
                    result.UnitPointsDeltas.Add(
                        (DisplayName(included[i]), includedRosters[i].BasePointCost, included[i].Cost));
                }
            }

            result.OurTotalPoints = ListCompiler.Compile(book, selections).TotalPoints;
            result.TheirTotalPoints = list.ListPoints is int lp && lp > 0 ? lp : included.Sum(d => d.Cost);
            result.TheirTotalIsAuthoritative = list.ListPoints is int v && v > 0;

            // listPoints covers the WHOLE list, excluded units included, and their share of it cannot be
            // backed out exactly (their `cost` is a base cost too). Say the comparison is off rather than
            // presenting a shortfall the user would read as a pricing bug.
            if (result.TheirTotalIsAuthoritative && excluded.Count > 0)
            {
                result.Warnings.Add($"{excluded.Count} unit(s) were excluded, so the Army Forge total " +
                    $"({result.TheirTotalPoints} pts) still counts them - it is not directly comparable to " +
                    "our total until they are replaced in the Forge.");
            }

            // Our compiler can only be as right as the book's option prices. Where OPR published none, a
            // total that disagrees is expected, not a bug in ListCompiler - say so rather than letting the
            // user read it as a #218-class defect.
            int unpriced = CountUnpricedChoices(book, selections);
            if (unpriced > 0)
            {
                result.UnpricedUpgradeCount = unpriced;
                result.Warnings.Add($"{unpriced} selected upgrade(s) have no published Army Forge price " +
                    "(#219) - our total counts them as free, so a shortfall against the Army Forge total " +
                    "is expected here.");
            }
            return result;
        }

        /// <summary>How many of the reconstructed choices land on an option OPR never priced (#219) - the
        /// reason our compiled total can legitimately fall short of Army Forge's.</summary>
        private static int CountUnpricedChoices(BookFile book, BuilderList selections)
        {
            int count = 0;
            foreach (BuilderUnit bu in selections.Units)
            {
                RosterUnit? roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
                if (roster is null) continue;
                foreach (UpgradeChoice choice in bu.Choices)
                {
                    UpgradeOption? option = roster.Sections
                        .FirstOrDefault(s => s.Id == choice.SectionId)?.Options
                        .FirstOrDefault(o => o.Id == choice.OptionId);
                    if (option?.CostUnpriced == true) count += Math.Max(1, choice.Count);
                }
            }
            return count;
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

        /// <summary>Army Forge's total. Authoritative when <see cref="TheirTotalIsAuthoritative"/> (taken
        /// from the list's `listPoints`); otherwise a fallback sum of BASE unit costs, which reads low.</summary>
        public int TheirTotalPoints { get; set; }
        public bool TheirTotalIsAuthoritative { get; set; }

        /// <summary>Selected upgrades OPR publishes no price for (#219). Non-zero means our total is
        /// expected to fall short of <see cref="TheirTotalPoints"/> through no fault of ListCompiler.</summary>
        public int UnpricedUpgradeCount { get; set; }
    }
}
