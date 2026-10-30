using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Dispatch;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Optional force-organization check (#003, 3.6 limits #430). Computes army-composition warnings against
    /// the OPR "Force Org. (optional)" caps for the army's game system (<see cref="ForceOrgLimits"/>: GDF and
    /// AoF print different numbers) but is purely advisory - it never blocks saving or launching.
    /// Pure and side-effect-free so it can be unit-tested and reused (e.g. by the lobby) without any UI.
    ///
    /// <para>The points basis every "per N pts" allowance reads is the army's points LIMIT (the size of game
    /// it is built for), or its actual total when no limit is set. Using the limit rather than the running
    /// total keeps a half-built list from tripping the 35% and unit-count caps on every row it adds, and
    /// matches how the Hero cap has always read (#003).</para>
    /// </summary>
    public static class ForceOrgValidator
    {
        /// <summary>
        /// Returns one human-readable warning for each force-org cap the army exceeds, in display order.
        /// An empty list means the army is within every cap. Never throws and never blocks.
        /// </summary>
        public static IReadOnlyList<string> Validate(ArmyListFile army)
        {
            List<string> warnings = new List<string>();
            ForceOrgLimits limits = ForceOrgLimits.For(army.GameSystem);
            int basis = ForcePointsBasis(army);

            // Cost cap - total points must not exceed the army's limit.
            if (army.TotalPoints > army.PointsLimit)
            {
                warnings.Add($"Over points limit: {army.TotalPoints} / {army.PointsLimit} pts.");
            }

            // Hero cap - 1 Hero per 500 pts (AoF: 375). A Unique Hero counts as a Hero (IsHero).
            int heroCount = army.Units.Count(IsHero);
            int heroAllowance = limits.MaxHeroes(basis);
            if (heroCount > heroAllowance)
            {
                warnings.Add($"Too many Heroes: {heroCount} (max {heroAllowance} at {basis} pts; 1 per {limits.PointsPerHero}).");
            }

            // Copy cap - up to 1+X copies of the same unit, X being 1 per 1000 pts (AoF: 750). A merged
            // combined pair is ONE copy toward the cap (Chris, 2026-07-10: doubled-up squads must not count
            // twice; 3.6 says the same: "combined units count as one"). ListCompiler folds a combined pair
            // into a single army-file entry, so counting entries counts it once. The compiler no longer
            // suffixes merged names (Chris, 2026-07-24), but army files written before then still contain
            // "X (Combined)" entries - the suffix strip keeps those grouping with plain "X" squads. Blank
            // names are skipped, since several half-built (unnamed) entries shouldn't read as duplicates of
            // each other.
            int copyAllowance = limits.MaxCopies(basis);
            foreach (IGrouping<string, UnitFileEntry> group in army.Units
                .Where(u => !string.IsNullOrWhiteSpace(u.Name))
                .GroupBy(u => BaseUnitName(u.Name), StringComparer.Ordinal)
                .Where(g => g.Count() > copyAllowance))
            {
                warnings.Add($"Too many copies of \"{group.Key}\": {group.Count()} (max {copyAllowance} at {basis} pts; 1 + 1 per {limits.PointsPerExtraCopy}).");
            }

            // Unit-size cap - no single unit worth over 35% of total points. A Hero who joins a unit is part
            // of that unit (it deploys and fights as one), so its cost counts toward the unit it joins.
            List<TableUnit> tableUnits = TableUnits(army);
            int unitPointsAllowance = limits.MaxUnitPoints(basis);
            foreach (TableUnit tableUnit in tableUnits.Where(t => t.Points > unitPointsAllowance))
            {
                warnings.Add($"Unit \"{tableUnit.Name}\" is too large: {tableUnit.Points} pts (max {unitPointsAllowance} at {basis} pts; {limits.MaxUnitSharePercent}% of the army).");
            }

            // Unit-count cap - max 1 unit per 200 pts (AoF: 150). Counted as units on the table, so a
            // combined pair is one (one entry) and a joined Hero is not a unit of its own.
            int unitAllowance = limits.MaxUnits(basis);
            if (tableUnits.Count > unitAllowance)
            {
                warnings.Add($"Too many units: {tableUnits.Count} (max {unitAllowance} at {basis} pts; 1 per {limits.PointsPerUnit}).");
            }

            // Model-or-Tough cap - max 1 model or Tough per 20 pts (AoF: 15). OWNER REVIEW (#430 D1): each
            // model counts max(1, its Tough), so a Tough(6) monster is 6 and a 10-model squad is 10.
            int modelWeight = army.Units.Sum(ModelOrToughWeight);
            int modelAllowance = limits.MaxModelsOrTough(basis);
            if (modelWeight > modelAllowance)
            {
                warnings.Add($"Too many models/Tough: {modelWeight} (max {modelAllowance} at {basis} pts; 1 model or Tough per {limits.PointsPerModelOrTough}).");
            }

            // Unique Hero - "this model may only be taken once per army". ListValidator turns this into an
            // Error for catalog-built lists (and drops this duplicate); an army with no catalog behind it
            // (hand-authored, imported) can only be warned.
            foreach (IGrouping<string, UnitFileEntry> group in army.Units
                .Where(u => !string.IsNullOrWhiteSpace(u.Name) && HasUniqueHeroRule(u))
                .GroupBy(u => BaseUnitName(u.Name), StringComparer.Ordinal)
                .Where(g => g.Count() > 1))
            {
                warnings.Add($"Unique Hero \"{group.Key}\" taken {group.Count()} times (once per army).");
            }

            // Unit cap - an army of only Heroes has no rank-and-file unit for them to lead. (An entirely
            // empty army is left alone - it's just unfinished, not mis-composed.)
            if (army.Units.Count > 0 && army.Units.All(IsHero))
            {
                warnings.Add("Army has no non-Hero units.");
            }

            return warnings;
        }

        /// <summary>The points every "per N pts" allowance is computed from: the army's limit, or its actual
        /// total when it has no limit set (an imported list may carry none).</summary>
        public static int ForcePointsBasis(ArmyListFile army) =>
            army.PointsLimit > 0 ? army.PointsLimit : army.TotalPoints;

        /// <summary>The copy-cap group key: the unit's name with the legacy " (Combined)" merge
        /// suffix stripped (written by pre-2026-07-24 compiles/imports), so an old file's combined
        /// pair still groups with plain squads of the same unit.</summary>
        private static string BaseUnitName(string name) =>
            name.EndsWith(" (Combined)", StringComparison.Ordinal)
                ? name[..^" (Combined)".Length]
                : name;

        /// <summary>One unit as it stands on the table: an army-file entry plus the cost of any Heroes
        /// joined to it.</summary>
        private readonly record struct TableUnit(string Name, int Points);

        /// <summary>
        /// The army's entries folded into table units: a Hero whose join target is an eligible host in this
        /// army rides in that host's unit (name and cost of the host, plus the Hero's cost); every other
        /// entry stands alone. Eligibility mirrors <c>HeroJoinResolver</c> / <c>ListValidator</c>'s
        /// hero-join checks: a single-model Hero within the join Tough cap, joining a different multi-model
        /// unit that is not itself a Hero. An ineligible join deploys solo at game time, so it stays a unit.
        /// </summary>
        private static List<TableUnit> TableUnits(ArmyListFile army)
        {
            Dictionary<string, UnitFileEntry> byId = new Dictionary<string, UnitFileEntry>(StringComparer.Ordinal);
            foreach (UnitFileEntry unit in army.Units)
            {
                if (!string.IsNullOrEmpty(unit.Id))
                {
                    byId.TryAdd(unit.Id, unit);
                }
            }

            Dictionary<UnitFileEntry, int> joinedCost = new Dictionary<UnitFileEntry, int>();
            HashSet<UnitFileEntry> absorbed = new HashSet<UnitFileEntry>();
            foreach (UnitFileEntry hero in army.Units)
            {
                if (string.IsNullOrEmpty(hero.JoinsUnitId)
                    || !byId.TryGetValue(hero.JoinsUnitId, out UnitFileEntry? host)
                    || !CanJoin(hero, host))
                {
                    continue;
                }

                absorbed.Add(hero);
                joinedCost[host] = joinedCost.GetValueOrDefault(host) + hero.PointCost;
            }

            return army.Units
                .Where(unit => !absorbed.Contains(unit))
                .Select(unit => new TableUnit(unit.Name, unit.PointCost + joinedCost.GetValueOrDefault(unit)))
                .ToList();
        }

        private static bool CanJoin(UnitFileEntry hero, UnitFileEntry host)
        {
            if (ReferenceEquals(hero, host) || !IsHero(hero) || hero.ModelCount != 1) return false;
            if (ListValidator.TryGetToughValue(hero, out int tough) && tough > HeroJoinResolver.MaxHeroToughForJoin) return false;
            return host.ModelCount > 1 && !IsHero(host);
        }

        /// <summary>The model-or-Tough weight of a unit: every model counts as one, or as its Tough when that
        /// is higher. A unit-wide Tough(X) applies to every model; <see cref="UnitFileEntry.ModelGrants"/>
        /// (#407) give some models a Tough of their own, and such a model counts the higher of its own and
        /// the unit's, as it does at army load.</summary>
        private static int ModelOrToughWeight(UnitFileEntry unit)
        {
            int models = Math.Max(0, unit.ModelCount);
            int unitTough = ListValidator.TryGetToughValue(unit, out int tough) ? tough : 0;
            int perModel = Math.Max(1, unitTough);
            int weight = models * perModel;

            int unassigned = models;
            foreach (ModelGrantEntry grant in unit.ModelGrants ?? new List<ModelGrantEntry>())
            {
                int count = Math.Min(Math.Max(0, grant.Count), unassigned);
                unassigned -= count;
                foreach (SpecialRuleEntry rule in grant.Rules)
                {
                    if (ListValidator.TryGetToughRule(rule, out int grantTough) && grantTough > perModel)
                    {
                        weight += count * (grantTough - perModel);
                        break;
                    }
                }
            }

            return weight;
        }

        /// <summary>True if the unit counts as a Hero: it carries the Hero rule, or a rule that counts as having
        /// it (Unique Hero, <see cref="CoreRuleCatalog.ImpliedRuleName"/>), recursing through alias entries.
        /// The single definition of "is this army-file unit a Hero", shared with the Army Builder UI.</summary>
        public static bool IsHero(UnitFileEntry unit) => HasHeroRule(unit.SpecialRules);

        /// <summary>Same test over a bare rule list - for callers that have rules but no unit entry (the
        /// default-base estimator).</summary>
        public static bool HasHeroRule(IEnumerable<SpecialRuleEntry> rules) => rules.Any(EntryIsHero);

        private static bool EntryIsHero(SpecialRuleEntry entry) => entry switch
        {
            SpecialRuleEntry_Core core => NamesHero(core.Name),
            SpecialRuleEntry_CoreNumeric num => NamesHero(num.Name),
            SpecialRuleEntry_Alias alias => EntryIsHero(alias.AliasedRule),
            _ => false,
        };

        private const string HeroRuleName = "Hero";

        private static bool NamesHero(string name) =>
            string.Equals(name, HeroRuleName, StringComparison.Ordinal)
            || string.Equals(CoreRuleCatalog.ImpliedRuleName(name), HeroRuleName, StringComparison.Ordinal);

        /// <summary>True if the unit carries the Unique Hero book rule (alias-aware).</summary>
        public static bool HasUniqueHeroRule(UnitFileEntry unit) => unit.SpecialRules.Any(EntryIsUniqueHero);

        private static bool EntryIsUniqueHero(SpecialRuleEntry entry) => entry switch
        {
            SpecialRuleEntry_Core core => string.Equals(core.Name, CoreRuleCatalog.UniqueHeroName, StringComparison.Ordinal),
            SpecialRuleEntry_Alias alias => EntryIsUniqueHero(alias.AliasedRule),
            _ => false,
        };
    }
}
