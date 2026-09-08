using System;
using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    /// <summary>
    /// The editing rules behind a <see cref="BuilderList"/>: adding and removing units, combining a
    /// squad with a second copy of itself (#107), choosing upgrade options under a section's own
    /// exclusivity, and finding the units a Hero may join (#006).
    ///
    /// <para>
    /// This is data logic, not UI. It lived on the Army Forge screen until #397 needed the same rules
    /// for the Combat Calculator's two unit columns; rather than let a second screen grow its own
    /// notion of what a legal edit is, it moved here beside <see cref="ListCompiler"/> and
    /// <see cref="ListValidator"/>, which compile and check the very lists it produces. The screens
    /// keep only their own selection bookkeeping.
    /// </para>
    /// </summary>
    public static class BuilderListEditing
    {
        /// <summary>
        /// Appends a roster unit at its default size. Returns its index, or -1 if the book has no such
        /// unit (a stale id from a switched book, which is not an error worth throwing over).
        /// </summary>
        public static int AddUnit(BookFile book, BuilderList list, string rosterUnitId)
        {
            RosterUnit? roster = book.Units.FirstOrDefault(unit => unit.Id == rosterUnitId);
            if (roster is null)
            {
                return -1;
            }

            list.Units.Add(new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount });
            return list.Units.Count - 1;
        }

        /// <summary>
        /// Removes a unit and clears any link that pointed at it, so a surviving combine or join partner
        /// is left a clean independent unit rather than one carrying a dangling reference. False if the
        /// index was out of range.
        /// </summary>
        public static bool RemoveUnit(BuilderList list, int index)
        {
            if (index < 0 || index >= list.Units.Count)
            {
                return false;
            }

            string? removedId = list.Units[index].Id;
            list.Units.RemoveAt(index);

            if (!string.IsNullOrEmpty(removedId))
            {
                foreach (BuilderUnit unit in list.Units)
                {
                    if (unit.CombinedWithId == removedId) unit.CombinedWithId = null;
                    if (unit.JoinsUnitId == removedId) unit.JoinsUnitId = null;
                }
            }

            return true;
        }

        /// <summary>The index of this unit's combine partner, or -1. The link is authored on the spawned
        /// copy, so either half resolves to the other; mirrors the validity test the compiler merges on.</summary>
        public static int CombinePartnerIndex(BuilderList list, int index)
        {
            if (index < 0 || index >= list.Units.Count)
            {
                return -1;
            }

            BuilderUnit unit = list.Units[index];
            for (int i = 0; i < list.Units.Count; i++)
            {
                if (i == index) continue;

                BuilderUnit other = list.Units[i];
                if (other.RosterUnitId != unit.RosterUnitId) continue;

                bool unitLinksOther = !string.IsNullOrEmpty(unit.CombinedWithId) && unit.CombinedWithId == other.Id;
                bool otherLinksUnit = !string.IsNullOrEmpty(other.CombinedWithId) && other.CombinedWithId == unit.Id;
                if (unitLinksOther || otherLinksUnit)
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool IsCombined(BuilderList list, int index) => CombinePartnerIndex(list, index) >= 0;

        /// <summary>A unit may be combined only if it is multi-model and not a Hero - the eligibility the
        /// compiler and validator both assume.</summary>
        public static bool CanCombine(BookFile book, BuilderList list, int index)
        {
            if (index < 0 || index >= list.Units.Count)
            {
                return false;
            }

            (UnitFileEntry unit, _) = ListCompiler.CompileUnitDetailed(book, list.Units[index]);
            return unit.ModelCount > 1 && !ForceOrgValidator.IsHero(unit);
        }

        /// <summary>
        /// The "Combined Unit" toggle. ON spawns a second identical copy right after this one, linked so
        /// the compiler merges the pair into one big unit; OFF removes the spawned copy and leaves the
        /// base a normal unit. Returns the index that should now be selected, or -1 when nothing changed.
        /// </summary>
        public static int SetCombined(BookFile book, BuilderList list, int index, bool on)
        {
            if (index < 0 || index >= list.Units.Count)
            {
                return -1;
            }

            int partner = CombinePartnerIndex(list, index);

            if (on)
            {
                if (partner >= 0 || !CanCombine(book, list, index))
                {
                    return -1;
                }

                BuilderUnit unit = list.Units[index];
                var copy = new BuilderUnit
                {
                    RosterUnitId = unit.RosterUnitId,
                    ModelCount = unit.ModelCount,
                    CombinedWithId = EnsureId(unit),
                };

                // Whole-unit picks start mirrored, rather than only syncing on the next edit.
                SeedMirroredChoices(book, unit, copy);
                list.Units.Insert(index + 1, copy);
                return index; // keep viewing the base copy
            }

            if (partner < 0)
            {
                return -1;
            }

            // Remove the SPAWNED copy (the one carrying the link); the base survives as a normal unit.
            int spawned = !string.IsNullOrEmpty(list.Units[index].CombinedWithId) ? index : partner;
            int survivor = spawned == index ? partner : index;
            RemoveUnit(list, spawned);

            return survivor > spawned ? survivor - 1 : survivor;
        }

        /// <summary>Indices of the units a Hero at <paramref name="heroIndex"/> may join: other
        /// multi-model, non-Hero units in the same list.</summary>
        public static List<int> HostCandidates(IReadOnlyList<UnitFileEntry> rows, int heroIndex)
        {
            var hosts = new List<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (i != heroIndex && rows[i].ModelCount > 1 && !ForceOrgValidator.IsHero(rows[i]))
                {
                    hosts.Add(i);
                }
            }

            return hosts;
        }

        /// <summary>The unit's author-stable id, generated on first demand (join and combine links use it).</summary>
        public static string EnsureId(BuilderUnit unit) => unit.Id ??= Guid.NewGuid().ToString("N");

        public static int ChoiceCount(BuilderUnit unit, string sectionId, string optionId) =>
            unit.Choices.FirstOrDefault(choice => choice.SectionId == sectionId && choice.OptionId == optionId)?.Count ?? 0;

        public static bool IsChosen(BuilderUnit unit, string sectionId, string optionId) =>
            ChoiceCount(unit, sectionId, optionId) > 0;

        /// <summary>
        /// Set (count &gt; 0) or clear (count == 0) an option. A single-select section is mutually
        /// exclusive - choosing one clears the section's other pick.
        /// </summary>
        public static void SetChoice(BuilderUnit unit, UpgradeSection section, string optionId, int count)
        {
            bool singleSelect = !section.IsCounted && section.MaxPicks <= 1;
            if (singleSelect)
            {
                unit.Choices.RemoveAll(choice => choice.SectionId == section.Id);
            }
            else
            {
                unit.Choices.RemoveAll(choice => choice.SectionId == section.Id && choice.OptionId == optionId);
            }

            if (count > 0)
            {
                unit.Choices.Add(new UpgradeChoice { SectionId = section.Id, OptionId = optionId, Count = count });
            }
        }

        /// <summary>
        /// Apply a choice, then - for a shared whole-unit (Affects=All) section of a combined pair -
        /// mirror that section onto the partner, so both halves carry the swap and both pay for it.
        /// </summary>
        public static void ApplyChoice(BuilderUnit unit, BuilderUnit? mirror, UpgradeSection section,
            string optionId, int count)
        {
            SetChoice(unit, section, optionId, count);
            if (mirror != null && section.Affects == UpgradeAffects.All)
            {
                MirrorSection(unit, mirror, section.Id);
            }
        }

        /// <summary>Replace one section's choices on <paramref name="to"/> with a clone of
        /// <paramref name="from"/>'s - a full resync, robust to multi-select and prior divergence.</summary>
        public static void MirrorSection(BuilderUnit from, BuilderUnit to, string sectionId)
        {
            to.Choices.RemoveAll(choice => choice.SectionId == sectionId);
            to.Choices.AddRange(from.Choices
                .Where(choice => choice.SectionId == sectionId)
                .Select(choice => new UpgradeChoice
                {
                    SectionId = choice.SectionId,
                    OptionId = choice.OptionId,
                    Count = choice.Count,
                }));
        }

        /// <summary>Seed a freshly spawned combined copy with the base's whole-unit choices.</summary>
        public static void SeedMirroredChoices(BookFile book, BuilderUnit from, BuilderUnit to)
        {
            RosterUnit? roster = book.Units.FirstOrDefault(unit => unit.Id == from.RosterUnitId);
            if (roster is null)
            {
                return;
            }

            foreach (UpgradeSection section in roster.Sections.Where(s => s.Affects == UpgradeAffects.All))
            {
                MirrorSection(from, to, section.Id);
            }
        }
    }
}
