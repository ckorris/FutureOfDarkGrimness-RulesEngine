using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using System;
using System.Collections.Generic;

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
        /// Registers an army's embedded #059 rule definitions into <paramref name="ruleResolver"/>,
        /// overriding any rule already registered under the same name (core rules register first, so a
        /// template's embedded definitions retune them by name). Must run before the army's unit/weapon
        /// rule names are resolved, so its own and overriding rules are available at lookup time.
        ///
        /// Every definition is validated first (#059 workstream 3): if any rule has a Condition/Effect
        /// whose required capability its hook's context can't provide, the whole load is rejected with a
        /// <see cref="RuleValidationException"/> listing every violation — nothing is registered, so the
        /// resolver is never left half-populated. A capability mismatch is an authoring bug in shipped
        /// data (distinct from <see cref="ResolveForScope"/>'s tolerance of valid-but-unimplemented
        /// rules) and must fail loudly rather than misbehave silently at dispatch.
        /// </summary>
        public static void RegisterEmbeddedDefinitions(RuleResolver ruleResolver, ArmyListFile armyListFile,
            RuleValidator? validator = null)
        {
            validator ??= new RuleValidator();

            List<RuleViolation> violations = new List<RuleViolation>();
            foreach (SpecialRuleDefinition definition in armyListFile.RuleDefinitions)
            {
                violations.AddRange(validator.Validate(definition));
            }

            if (violations.Count > 0)
            {
                throw new RuleValidationException(armyListFile.Name, violations);
            }

            foreach (SpecialRuleDefinition definition in armyListFile.RuleDefinitions)
            {
                ruleResolver.RegisterOrReplace(definition);
            }
        }

        /// <summary>
        /// Resolves <paramref name="ruleEntry"/> against <paramref name="ruleResolver"/> and
        /// returns the attachment-ready <see cref="ResolvedRule"/> (requested name preserved
        /// for alias display, per-instance arguments carried). Returns null — with a
        /// <see cref="RuleDiagnostics"/> warning, so partial armies still load — when the rule has no definition in the
        /// registry (valid but not yet implemented) or when its declared
        /// <see cref="SpecialRuleDefinition.Scope"/> doesn't match
        /// <paramref name="attachmentScope"/> (a weapon rule named at unit level, or vice
        /// versa, is misauthored data and must not attach where it doesn't belong).
        /// </summary>
        public static ResolvedRule? ResolveForScope(IRuleResolver ruleResolver, SpecialRuleEntry ruleEntry,
            ERuleScope attachmentScope, string ownerDescription)
        {
            ResolvedRule? resolved = ResolveOrDescribeDrop(ruleResolver, ruleEntry, attachmentScope,
                ownerDescription, out RuleDrop? drop);
            if (drop != null)
            {
                RuleDiagnostics.WarnDropped(drop.Value);
            }

            return resolved;
        }

        /// <summary>
        /// <see cref="ResolveForScope"/> without the scope gate: resolves the name and checks argument
        /// arity, leaving the caller to read <see cref="SpecialRuleDefinition.Scope"/> and decide where the
        /// rule belongs. Still returns null (with a warning) for an unimplemented name or a missing numeric
        /// argument, since neither can attach anywhere.
        ///
        /// The unit-level attachment path (#197 slice 0) needs this: wargear is a rule-bundle folded into
        /// the unit's rule list, so a weapon rule granted by an item arrives named at unit scope and must be
        /// re-homed onto the unit's weapons rather than dropped. Weapon-level attachment keeps using
        /// <see cref="ResolveForScope"/> — a unit rule named on a weapon profile really is misauthored data
        /// and has nowhere to go.
        /// </summary>
        public static ResolvedRule? ResolveAnyScope(IRuleResolver ruleResolver, SpecialRuleEntry ruleEntry,
            string ownerDescription)
        {
            ResolvedRule? resolved = ResolveOrDescribeDrop(ruleResolver, ruleEntry, attachmentScope: null,
                ownerDescription, out RuleDrop? drop);
            if (drop != null)
            {
                RuleDiagnostics.WarnDropped(drop.Value);
            }

            return resolved;
        }

        /// <summary>
        /// The single classification ladder behind <see cref="ResolveForScope"/> /
        /// <see cref="ResolveAnyScope"/>, WITHOUT the warning side effect: resolves the name, checks
        /// argument arity, then (when <paramref name="attachmentScope"/> is non-null) gates on scope.
        /// On failure returns null and describes why in <paramref name="drop"/>; the live attachment
        /// paths raise it through <see cref="RuleDiagnostics.WarnDropped"/>, while
        /// <see cref="ArmyRuleAudit"/> (#168) collects it silently — sharing this ladder is what keeps
        /// the army-builder audit from drifting out of sync with what actually attaches at launch.
        /// </summary>
        public static ResolvedRule? ResolveOrDescribeDrop(IRuleResolver ruleResolver, SpecialRuleEntry ruleEntry,
            ERuleScope? attachmentScope, string ownerDescription, out RuleDrop? drop)
        {
            (string lookupName, IReadOnlyList<RuleArgument> arguments) = DescribeRuleEntry(ruleEntry);

            if (!ruleResolver.TryResolve(lookupName, out ResolvedRule resolved))
            {
                drop = new RuleDrop(ruleEntry.PrintableName, ownerDescription,
                    ERuleDropReason.Unimplemented,
                    $"Skipping unimplemented special rule '{ruleEntry.PrintableName}' on {ownerDescription}.");
                return null;
            }

            int maxArgIndex = RuleArgumentArity.MaxReferencedArgIndex(resolved.Definition);
            if (maxArgIndex >= arguments.Count)
            {
                drop = new RuleDrop(ruleEntry.PrintableName, ownerDescription,
                    ERuleDropReason.MissingArgument,
                    $"Skipping special rule '{ruleEntry.PrintableName}' on {ownerDescription}: " +
                    $"its effects read Arg({maxArgIndex}) but the entry supplies only {arguments.Count} " +
                    "argument(s) - a numeric value is likely missing from the army-list reference.");
                return null;
            }

            if (attachmentScope != null && resolved.Definition.Scope != attachmentScope)
            {
                drop = new RuleDrop(ruleEntry.PrintableName, ownerDescription,
                    ERuleDropReason.WrongScope,
                    $"Skipping special rule '{ruleEntry.PrintableName}' on {ownerDescription}: " +
                    $"it is a {resolved.Definition.Scope}-scoped rule and can't attach at {attachmentScope} scope.");
                return null;
            }

            drop = null;
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
                case SpecialRuleEntry_Text text:
                    return (text.Name, new RuleArgument[] { new RuleArgument.Str(text.TextValue) });
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
