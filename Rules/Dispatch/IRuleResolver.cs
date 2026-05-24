using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Read-side interface for looking up <see cref="SpecialRuleDefinition"/>s by name.
/// Callers that need to populate the registry hold a concrete
/// <see cref="RuleResolver"/> reference instead.
///
/// Returns a <see cref="ResolvedRule"/> wrapper rather than a bare definition so
/// that the caller's requested name is preserved alongside the resolved
/// definition, even when the name was an alias — enabling identity-based
/// comparisons via <see cref="ResolvedRule.Definition"/> and alias-aware display
/// via <see cref="ResolvedRule.RequestedName"/>.
/// </summary>
public interface IRuleResolver
{
    /// <summary>
    /// Looks up the rule registered under <paramref name="rule"/>. The name may be
    /// either a canonical <see cref="SpecialRuleDefinition.Name"/> or an alias
    /// previously registered via <see cref="RuleResolver.RegisterAlias"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No rule is registered under this name.</exception>
    ResolvedRule Resolve(string rule);
}
