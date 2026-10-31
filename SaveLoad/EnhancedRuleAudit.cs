using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.SaveLoad
{
    /// <summary>
    /// #441 F5 (#433 hand-off, signed off B): "Enhanced X does nothing without X". OPR 3.6's Enhanced family is
    /// authored as an increment gated on the bearer carrying its base rule (<see cref="Condition.UnitHasRule"/> /
    /// <see cref="Condition.WeaponHasRule"/> inside the definition - #433 Option B), so a hand-edited list that gives
    /// a unit Enhanced X without X loads fine and the rule is silently inert. Army Forge only offers Enhanced X where
    /// X is present, so this only arises in the Army Builder, whose validation pane shows the lines. Advisory - a
    /// sibling of <see cref="ArmyRuleAudit"/>, never a launch blocker and not a #168 drop (the rule DOES attach).
    ///
    /// <para>The base rule is read off the Enhanced rule's own definition - the same gate the evaluator reads -
    /// never parsed from its name (<see cref="EnhancedRuleGate"/>): an Enhanced rule that resolves to a definition
    /// with no bearer-rule gate (an aura that grants the Enhanced rule to others) depends on nothing and says
    /// nothing, and one that does not resolve at all is <see cref="ArmyRuleAudit"/>'s business. Gates under a
    /// <see cref="Condition.Not"/> are not dependencies; a gate on a one-activation helper ("Watchborn (Piercing)")
    /// is a dependency on the rule whose activation pick grants it (Watchborn), which is what the unit carries
    /// (#441 F7). "Carries X" is what <see cref="Condition.UnitHasRule.Holds"/> would see at game time: the unit's
    /// own rules, its weapons' rules, its #407 model grants, and the rules of a Hero joined to it (or of the unit a
    /// Hero joins) - a joined Hero is one of the unit's models. Aura-granted rules cannot be seen from a list and
    /// may produce a false advisory; acceptable for an advisory pane.</para>
    /// </summary>
    public static class EnhancedRuleAudit
    {
        /// <summary>The 3.6 family's naming: every member is "Enhanced " + its base (plus an Aura/Buff suffix).</summary>
        public const string Prefix = EnhancedRuleGate.Prefix;

        /// <summary>One advisory line per (unit, Enhanced rule) whose base rule the unit does not carry, in list
        /// order: "Warriors: Enhanced Lustbound does nothing without Lustbound". Empty for a clean list.</summary>
        public static IReadOnlyList<string> Audit(ArmyListFile armyListFile)
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            try
            {
                ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, armyListFile);
            }
            catch (RuleValidationException)
            {
                // ArmyRuleAudit reports the broken embedded set; the core-catalog-only audit still runs here.
            }

            Dictionary<string, UnitFileEntry> byId = new Dictionary<string, UnitFileEntry>(StringComparer.Ordinal);
            foreach (UnitFileEntry unit in armyListFile.Units)
            {
                if (!string.IsNullOrEmpty(unit.Id)) byId.TryAdd(unit.Id, unit);
            }

            List<string> lines = new List<string>();
            foreach (UnitFileEntry unit in armyListFile.Units)
            {
                HashSet<string> carried = new HashSet<string>(OwnRuleNames(unit), StringComparer.OrdinalIgnoreCase);
                foreach (UnitFileEntry partner in JoinedPartners(unit, armyListFile, byId))
                {
                    carried.UnionWith(OwnRuleNames(partner));
                }

                foreach (string enhanced in OwnRuleNames(unit)
                             .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    IReadOnlyList<string> missing = BaseRuleNames(resolver, enhanced)
                        .Where(b => !carried.Contains(b))
                        .ToList();
                    if (missing.Count > 0)
                    {
                        lines.Add($"{unit.Name}: {enhanced} does nothing without {string.Join(" and ", missing)}");
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// The base rules of an Enhanced rule, read off its definition's gate (<see cref="EnhancedRuleGate.BaseRuleNames(SpecialRuleDefinition, IRuleResolver)"/>:
        /// <see cref="Condition.UnitHasRule"/> / <see cref="Condition.WeaponHasRule"/> through And/Or, not under Not,
        /// not the rule itself; a one-activation helper folded to the rule whose activation pick grants it), in
        /// first-seen order. Empty when the name does not resolve or the definition has no such gate.
        /// </summary>
        public static IReadOnlyList<string> BaseRuleNames(IRuleResolver resolver, string enhancedName)
        {
            if (!resolver.TryResolve(enhancedName, out ResolvedRule resolved))
            {
                return Array.Empty<string>();
            }

            return EnhancedRuleGate.BaseRuleNames(resolved.Definition, resolver)
                .Where(n => !string.Equals(n, enhancedName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Every rule name the unit itself carries, at every level UnitHasRule.Holds reads: unit rules,
        /// weapon rules, model-grant rules. Aliases resolve to the rule they rename; numeric rules to their bare name.</summary>
        private static IEnumerable<string> OwnRuleNames(UnitFileEntry unit)
        {
            foreach (SpecialRuleEntry rule in unit.SpecialRules) yield return LookupName(rule);
            foreach (WeaponFileEntry weapon in unit.Weapons)
                foreach (SpecialRuleEntry rule in weapon.SpecialRules) yield return LookupName(rule);
            foreach (ModelGrantEntry grant in unit.ModelGrants ?? new List<ModelGrantEntry>())
                foreach (SpecialRuleEntry rule in grant.Rules) yield return LookupName(rule);
        }

        private static string LookupName(SpecialRuleEntry rule) => ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName;

        /// <summary>The unit a Hero joins, or the Heroes joined to this unit - the models that share its table unit.</summary>
        private static IEnumerable<UnitFileEntry> JoinedPartners(UnitFileEntry unit, ArmyListFile army,
            Dictionary<string, UnitFileEntry> byId)
        {
            if (!string.IsNullOrEmpty(unit.JoinsUnitId) && byId.TryGetValue(unit.JoinsUnitId, out UnitFileEntry? host)
                && !ReferenceEquals(host, unit))
            {
                yield return host;
            }

            if (string.IsNullOrEmpty(unit.Id)) yield break;
            foreach (UnitFileEntry hero in army.Units)
            {
                if (!ReferenceEquals(hero, unit) && string.Equals(hero.JoinsUnitId, unit.Id, StringComparison.Ordinal))
                {
                    yield return hero;
                }
            }
        }
    }
}
