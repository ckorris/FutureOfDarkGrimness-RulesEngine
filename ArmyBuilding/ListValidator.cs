using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;

namespace FDG.ArmyBuilding
{
    // #153 (P4) — legality/validation for a catalog-built list. Pure and headless-testable. Reuses the engine's
    // ForceOrgValidator for army-composition caps (points / hero / copy / all-heroes) and adds the checks that
    // need the book: per-unit model-count range and per-section pick caps. Advisory — it reports issues; it does
    // not block. (The builder surfaces them; hard launch-blocking is a separate concern.)
    public enum ListIssueSeverity { Error, Warning }

    /// <param name="UnitIndex">Index into the list's units this issue is about, or -1 for army-wide.</param>
    public sealed record ListIssue(string Message, ListIssueSeverity Severity, int UnitIndex = -1);

    public static class ListValidator
    {
        public static IReadOnlyList<ListIssue> Validate(BookFile book, BuilderList list, BuiltArmyFile compiled)
        {
            var issues = new List<ListIssue>();

            // Points limit — an Error (the hard legality line).
            if (compiled.TotalPoints > list.PointsLimit)
                issues.Add(new ListIssue($"Over points limit: {compiled.TotalPoints} / {list.PointsLimit} pts.",
                    ListIssueSeverity.Error));

            // Catalog checks that ForceOrgValidator can't do — they need the roster.
            for (int i = 0; i < list.Units.Count && i < compiled.Units.Count; i++)
            {
                BuilderUnit bu = list.Units[i];
                RosterUnit? roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
                if (roster is null)
                {
                    issues.Add(new ListIssue($"Unknown roster unit '{bu.RosterUnitId}'.", ListIssueSeverity.Error, i));
                    continue;
                }

                int models = compiled.Units[i].ModelCount;
                if (models < roster.MinModels || models > roster.MaxModels)
                    issues.Add(new ListIssue(
                        $"{roster.Name}: {models} models (allowed {roster.MinModels}–{roster.MaxModels}).",
                        ListIssueSeverity.Error, i));

                foreach (UpgradeSection section in roster.Sections)
                {
                    if (section.IsCounted) continue; // counted sections are bounded by their stepper, not pick count
                    int picks = bu.Choices.Count(c => c.SectionId == section.Id);
                    int max = section.MaxPicks < 1 ? 1 : section.MaxPicks;
                    if (picks > max)
                        issues.Add(new ListIssue(
                            $"{roster.Name}: too many options in \"{section.Label}\" ({picks}/{max}).",
                            ListIssueSeverity.Error, i));
                }
            }

            // Army-composition caps (reuse the engine validator) as Warnings. The points cap is already an Error
            // above, so drop its duplicate from the warning set.
            foreach (string warning in ForceOrgValidator.Validate(compiled))
                if (!warning.StartsWith("Over points limit"))
                    issues.Add(new ListIssue(warning, ListIssueSeverity.Warning));

            return issues;
        }
    }
}
