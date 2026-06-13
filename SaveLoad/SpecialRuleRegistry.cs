using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Source of truth for what the army-builder rule picker offers, DERIVED from the engine's
    /// <see cref="CoreRuleCatalog"/> (#059 workstream 6) plus any #059 embedded rules carried by the
    /// army being edited. Previously this was a second, hand-maintained list that had drifted from the
    /// catalog — offering rules the engine doesn't implement (Sniper, Flying, …) and hiding ones it does
    /// (Strafing, Vanguard, …). Deriving from the catalog removes that drift: the picker offers exactly
    /// the rules that actually dispatch, and unimplemented rules simply aren't offered.
    ///
    /// A rule is "numeric" (carries a value like Tough(3)) iff its definition reads an argument, so the
    /// numeric/plain split is derived from the engine too (<see cref="RuleArgumentArity"/>) rather than
    /// a separate hand-list. Embedded definitions override core by name (same precedence as load-time
    /// registration), so an army that retunes a core rule sees its own version in the picker.
    /// </summary>
    public static class SpecialRuleRegistry
    {
        /// <summary>
        /// One offerable rule: its canonical name, whether it requires a numeric value, and the
        /// <see cref="ERuleScope"/> it attaches at. The scope lets the builder offer a rule only where
        /// it can legally attach — weapon-scoped rules on weapons, unit-scoped rules on units — so a
        /// mis-scoped placement (which load-time resolution silently skips) becomes impossible to author.
        /// </summary>
        public readonly record struct PickerEntry(string Name, bool IsNumeric, ERuleScope Scope);

        /// <summary> A rule needs a numeric value iff some effect of its definition reads an argument. </summary>
        public static bool DefinitionIsNumeric(SpecialRuleDefinition definition) =>
            RuleArgumentArity.MaxReferencedArgIndex(definition) >= 0;

        /// <summary>
        /// Every <see cref="CoreRuleCatalog"/> rule plus the supplied embedded definitions (which
        /// override core by name), each tagged numeric-or-not and with its <see cref="ERuleScope"/>,
        /// sorted by name for stable display. Pass <paramref name="scope"/> to get only the rules that
        /// attach at that scope (the builder's weapon list vs unit list); omit it for all rules.
        /// </summary>
        public static IReadOnlyList<PickerEntry> GetPickerEntries(
            IEnumerable<SpecialRuleDefinition>? embeddedDefinitions = null, ERuleScope? scope = null)
        {
            Dictionary<string, SpecialRuleDefinition> byName = new Dictionary<string, SpecialRuleDefinition>(
                StringComparer.Ordinal);

            foreach (SpecialRuleDefinition definition in CoreRuleCatalog.All)
            {
                byName[definition.Name] = definition;
            }

            if (embeddedDefinitions != null)
            {
                foreach (SpecialRuleDefinition definition in embeddedDefinitions)
                {
                    byName[definition.Name] = definition;
                }
            }

            return byName.Values
                .Where(d => scope == null || d.Scope == scope.Value)
                .Select(d => new PickerEntry(d.Name, DefinitionIsNumeric(d), d.Scope))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
