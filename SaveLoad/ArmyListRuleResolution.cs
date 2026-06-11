using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Shared army-load logic for turning a <see cref="SpecialRuleEntry"/> named on an army
    /// list into an attached #042 <see cref="ResolvedRule"/>. Used by FDGServer for unit-level
    /// rules and by <see cref="UnitData"/> for weapon-level rules (#027), so both attachment
    /// sites resolve names, carry arguments, and enforce scope identically.
    /// </summary>
    public static class ArmyListRuleResolution
    {
        /// <summary>
        /// Resolves <paramref name="ruleEntry"/> against <paramref name="ruleResolver"/> and
        /// returns the attachment-ready <see cref="ResolvedRule"/> (requested name preserved
        /// for alias display, per-instance arguments carried). Returns null — with a debug
        /// warning, so partial armies still load — when the rule has no definition in the
        /// registry (valid but not yet implemented) or when its declared
        /// <see cref="SpecialRuleDefinition.Scope"/> doesn't match
        /// <paramref name="attachmentScope"/> (a weapon rule named at unit level, or vice
        /// versa, is misauthored data and must not attach where it doesn't belong).
        /// </summary>
        public static ResolvedRule? ResolveForScope(IRuleResolver ruleResolver, SpecialRuleEntry ruleEntry,
            ERuleScope attachmentScope, string ownerDescription)
        {
            (string lookupName, IReadOnlyList<RuleArgument> arguments) = DescribeRuleEntry(ruleEntry);

            if (!ruleResolver.TryResolve(lookupName, out ResolvedRule resolved))
            {
                Debug.WriteLine(
                    $"[#042] Skipping unimplemented special rule '{ruleEntry.PrintableName}' on {ownerDescription}.");
                return null;
            }

            if (resolved.Definition.Scope != attachmentScope)
            {
                Debug.WriteLine(
                    $"[#027] Skipping special rule '{ruleEntry.PrintableName}' on {ownerDescription}: " +
                    $"it is a {resolved.Definition.Scope}-scoped rule and can't attach at {attachmentScope} scope.");
                return null;
            }

            return new ResolvedRule(ruleEntry.PrintableName, resolved.Definition, arguments);
        }

        /// <summary>
        /// Maps an army-list rule entry to the canonical name used for registry lookup plus any
        /// per-instance arguments. Core rules look up by name; numeric rules carry their value as a
        /// single Int argument; aliases look up by the rule they rename.
        /// </summary>
        public static (string lookupName, IReadOnlyList<RuleArgument> arguments) DescribeRuleEntry(SpecialRuleEntry ruleEntry)
        {
            switch (ruleEntry)
            {
                case SpecialRuleEntry_CoreNumeric numeric:
                    return (numeric.Name, new RuleArgument[] { new RuleArgument.Int(numeric.NumericValue) });
                case SpecialRuleEntry_Alias alias:
                    return DescribeRuleEntry(alias.AliasedRule);
                case SpecialRuleEntry_Core core:
                    return (core.Name, Array.Empty<RuleArgument>());
                default:
                    return (ruleEntry.PrintableName, Array.Empty<RuleArgument>());
            }
        }
    }
}
