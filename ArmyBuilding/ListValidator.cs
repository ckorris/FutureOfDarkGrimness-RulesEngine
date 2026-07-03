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

            // Per-unit compiles for the catalog checks: `compiled.Units` no longer aligns positionally with
            // `list.Units` once #107 combined pairs merge, so each list row is compiled individually here
            // (also what the author sees per row). Rows that fail to compile are flagged as unknown-roster.
            var perUnit = new List<UnitFileEntry?>(list.Units.Count);
            foreach (BuilderUnit bu in list.Units)
            {
                RosterUnit? roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
                perUnit.Add(roster is null ? null : ListCompiler.CompileUnitDetailed(book, bu).Unit);
            }

            // Catalog checks that ForceOrgValidator can't do — they need the roster.
            for (int i = 0; i < list.Units.Count; i++)
            {
                BuilderUnit bu = list.Units[i];
                RosterUnit? roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
                if (roster is null || perUnit[i] is null)
                {
                    issues.Add(new ListIssue($"Unknown roster unit '{bu.RosterUnitId}'.", ListIssueSeverity.Error, i));
                    continue;
                }

                int models = perUnit[i]!.ModelCount;
                if (models < roster.MinModels || models > roster.MaxModels)
                    issues.Add(new ListIssue(
                        $"{roster.Name}: {models} models (allowed {roster.MinModels}-{roster.MaxModels}).",
                        ListIssueSeverity.Error, i));

                // Unique — "This unit may only be taken once per army." An absolute list-building rule
                // (unlike the size-dependent force-org copy caps below, which stay Warnings), so each
                // copy beyond the first is an Error.
                if (HasRule(roster.Rules, "Unique")
                    && list.Units.Take(i).Any(prev => prev.RosterUnitId == bu.RosterUnitId))
                    issues.Add(new ListIssue(
                        $"{roster.Name}: Unique - the army may only include one copy.",
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

            ValidateCombinedLinks(book, list, issues);

            // Hero-join + composition checks run on the UNMERGED per-row units: GDF counts a combined unit
            // as two copies for composition caps, and join targets are authored against row ids (the
            // compiler remaps a join into a merged host at compile time).
            var unmerged = new BuiltArmyFile { PointsLimit = list.PointsLimit };
            unmerged.Units.AddRange(perUnit.Where(u => u is not null)!);

            ValidateHeroJoins(unmerged, issues);

            // Army-composition caps (reuse the engine validator) as Warnings. The points cap is already an Error
            // above, so drop its duplicate from the warning set.
            foreach (string warning in ForceOrgValidator.Validate(unmerged))
                if (!warning.StartsWith("Over points limit"))
                    issues.Add(new ListIssue(warning, ListIssueSeverity.Warning));

            return issues;
        }

        // #107 combined squads: flag link shapes the compiler deliberately refuses to merge (dangling id,
        // different roster unit) or that GDF disallows (more than two copies in one combine; single-model
        // units). Warnings — the list still compiles and plays, just as separate units.
        private static void ValidateCombinedLinks(BookFile book, BuilderList list, List<ListIssue> issues)
        {
            for (int i = 0; i < list.Units.Count; i++)
            {
                BuilderUnit bu = list.Units[i];
                if (string.IsNullOrEmpty(bu.CombinedWithId)) continue;

                RosterUnit? roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
                string name = roster?.Name ?? bu.RosterUnitId;

                BuilderUnit? host = list.Units.FirstOrDefault(other =>
                    other != bu && other.Id == bu.CombinedWithId);
                if (host is null || bu.CombinedWithId == bu.Id)
                {
                    issues.Add(new ListIssue($"{name}: combine target no longer exists; it stays a separate unit.",
                        ListIssueSeverity.Warning, i));
                    continue;
                }

                if (host.RosterUnitId != bu.RosterUnitId)
                {
                    issues.Add(new ListIssue(
                        $"{name}: only two copies of the SAME unit may combine; it stays a separate unit.",
                        ListIssueSeverity.Warning, i));
                    continue;
                }

                if (roster is not null && roster.BaseModelCount <= 1)
                    issues.Add(new ListIssue($"{name}: single-model units can't combine.",
                        ListIssueSeverity.Warning, i));

                // More than two copies in one combine: the host is itself combined into something, or a
                // third copy also targets the same host.
                if (!string.IsNullOrEmpty(host.CombinedWithId)
                    || list.Units.Any(other => other != bu && other != host && other.CombinedWithId == bu.CombinedWithId))
                    issues.Add(new ListIssue($"{name}: only two copies may combine into one unit.",
                        ListIssueSeverity.Warning, i));
            }
        }

        // #006 hero-join eligibility, mirrored so the builder can't author a join that silently deploys solo
        // at game time. Warnings, not Errors — the engine tolerates all of these (the hero just stays solo).
        private static void ValidateHeroJoins(BuiltArmyFile compiled, List<ListIssue> issues)
        {
            for (int i = 0; i < compiled.Units.Count; i++)
            {
                UnitFileEntry unit = compiled.Units[i];
                if (string.IsNullOrEmpty(unit.JoinsUnitId)) continue;

                if (!ForceOrgValidator.IsHero(unit))
                {
                    issues.Add(new ListIssue($"{unit.Name}: has a join target but no Hero rule.",
                        ListIssueSeverity.Warning, i));
                    continue;
                }
                if (unit.ModelCount != 1)
                    issues.Add(new ListIssue($"{unit.Name}: a Hero must be a single model to join a unit.",
                        ListIssueSeverity.Warning, i));
                if (TryGetToughValue(unit, out int tough) && tough > Rules.Dispatch.HeroJoinResolver.MaxHeroToughForJoin)
                    issues.Add(new ListIssue(
                        $"{unit.Name}: Tough({tough}) exceeds the join cap (Tough({Rules.Dispatch.HeroJoinResolver.MaxHeroToughForJoin})); it will deploy solo.",
                        ListIssueSeverity.Warning, i));

                UnitFileEntry? host = compiled.Units.FirstOrDefault(u =>
                    !ReferenceEquals(u, unit) && u.Id == unit.JoinsUnitId);
                if (host is null)
                    issues.Add(new ListIssue($"{unit.Name}: join target no longer exists; it will deploy solo.",
                        ListIssueSeverity.Warning, i));
                else if (host.ModelCount <= 1 || ForceOrgValidator.IsHero(host))
                    issues.Add(new ListIssue(
                        $"{unit.Name}: join target \"{host.Name}\" is ineligible (must be a multi-model unit without another Hero).",
                        ListIssueSeverity.Warning, i));
            }
        }

        /// <summary>Whether any entry carries the named rule (alias-aware: matches the alias's own name
        /// and recurses into the aliased rule). Case-insensitive, like the rule resolver.</summary>
        private static bool HasRule(IEnumerable<SpecialRuleEntry> rules, string name) =>
            rules.Any(entry => EntryMatches(entry, name));

        private static bool EntryMatches(SpecialRuleEntry entry, string name) => entry switch
        {
            SpecialRuleEntry_Core core => string.Equals(core.Name, name, System.StringComparison.OrdinalIgnoreCase),
            SpecialRuleEntry_CoreNumeric num => string.Equals(num.Name, name, System.StringComparison.OrdinalIgnoreCase),
            SpecialRuleEntry_Alias alias => string.Equals(alias.Name, name, System.StringComparison.OrdinalIgnoreCase)
                || EntryMatches(alias.AliasedRule, name),
            _ => false,
        };

        /// <summary>Reads a unit's Tough(X) value from its special rules (alias-aware). Shared with the
        /// builder UI. False when the unit has no Tough rule.</summary>
        public static bool TryGetToughValue(UnitFileEntry unit, out int tough)
        {
            foreach (SpecialRuleEntry entry in unit.SpecialRules)
                if (TryGetTough(entry, out tough))
                    return true;
            tough = 0;
            return false;
        }

        private static bool TryGetTough(SpecialRuleEntry entry, out int tough)
        {
            switch (entry)
            {
                case SpecialRuleEntry_CoreNumeric num when num.Name == "Tough":
                    tough = num.NumericValue; return true;
                case SpecialRuleEntry_Alias alias:
                    return TryGetTough(alias.AliasedRule, out tough);
                default:
                    tough = 0; return false;
            }
        }
    }
}
