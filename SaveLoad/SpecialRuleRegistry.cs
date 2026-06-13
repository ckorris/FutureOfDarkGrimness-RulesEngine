using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;

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
        /// <summary> One offerable rule: its canonical name and whether it requires a numeric value. </summary>
        public readonly record struct PickerEntry(string Name, bool IsNumeric);

        /// <summary> A rule needs a numeric value iff some effect of its definition reads an argument. </summary>
        public static bool DefinitionIsNumeric(SpecialRuleDefinition definition) =>
            RuleArgumentArity.MaxReferencedArgIndex(definition) >= 0;

        /// <summary>
        /// Every <see cref="CoreRuleCatalog"/> rule plus the supplied embedded definitions (which
        /// override core by name), each tagged numeric-or-not, sorted by name for stable display.
        /// </summary>
        public static IReadOnlyList<PickerEntry> GetPickerEntries(
            IEnumerable<SpecialRuleDefinition>? embeddedDefinitions = null)
        {
            Dictionary<string, bool> numericByName = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (SpecialRuleDefinition definition in CoreRuleCatalog.All)
            {
                numericByName[definition.Name] = DefinitionIsNumeric(definition);
            }

            if (embeddedDefinitions != null)
            {
                foreach (SpecialRuleDefinition definition in embeddedDefinitions)
                {
                    numericByName[definition.Name] = DefinitionIsNumeric(definition);
                }
            }

            return numericByName
                .Select(kv => new PickerEntry(kv.Key, kv.Value))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
