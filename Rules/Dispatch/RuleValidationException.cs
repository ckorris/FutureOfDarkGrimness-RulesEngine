using System;
using System.Collections.Generic;
using System.Text;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Thrown at army load (#059) when one or more embedded <see cref="SpecialRuleDefinition"/>s
/// fail <see cref="RuleValidator"/> — a Condition or Effect requires a capability the hook's
/// context can't provide, so the rule would silently misbehave at dispatch. Unlike
/// <c>ArmyListRuleResolution.ResolveForScope</c>'s skip-with-warning (which tolerates a
/// *valid-but-unimplemented* rule so partial armies still load), a validation violation is an
/// authoring bug in shipped data and is surfaced loudly: the army — and the game — won't start
/// until the file is fixed. The message lists every violation across the army's rules.
/// </summary>
public sealed class RuleValidationException : Exception
{
    public IReadOnlyList<RuleViolation> Violations { get; }

    public RuleValidationException(string armyName, IReadOnlyList<RuleViolation> violations)
        : base(BuildMessage(armyName, violations))
    {
        Violations = violations;
    }

    private static string BuildMessage(string armyName, IReadOnlyList<RuleViolation> violations)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"Army '{armyName}' has {violations.Count} invalid embedded rule ");
        sb.Append(violations.Count == 1 ? "violation:" : "violations:");
        foreach (RuleViolation v in violations)
        {
            sb.AppendLine();
            sb.Append($"  {v.RuleName} @ {v.Hook}: {v.Member} needs {v.MissingCapability.Name}, " +
                      "hook can't provide it");
        }

        return sb.ToString();
    }
}
