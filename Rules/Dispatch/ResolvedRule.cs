using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// The result of looking up a rule by name via <see cref="IRuleResolver"/>. Pairs
/// the <see cref="SpecialRuleDefinition"/> that was found with the
/// <see cref="RequestedName"/> the caller used to find it — which may be an alias
/// rather than the definition's canonical <see cref="SpecialRuleDefinition.Name"/> —
/// and the per-instance <see cref="Arguments"/> supplied where the rule was attached.
///
/// The fields serve distinct roles:
/// <list type="bullet">
///   <item><see cref="Definition"/> is used for identity comparisons. For example,
///         <c>Effect.IgnoreRule("Regeneration")</c> matches any attached rule whose
///         <see cref="Definition"/> is the Regeneration definition, including units
///         that authored it under the alias "Healing Pods".</item>
///   <item><see cref="RequestedName"/> is used for display. UI can show
///         "Healing Pods (Regeneration)" when the requested name differs from the
///         canonical one.</item>
///   <item><see cref="Arguments"/> are the parameterized values for this attachment
///         (Deadly's <c>[Int(3)]</c>, Tough's <c>[Int(6)]</c>). Empty for the many
///         rules that take none. Effects resolve <c>ValueSource.Arg(i)</c> against
///         this list at dispatch time.</item>
/// </list>
/// </summary>
public record ResolvedRule(string RequestedName, SpecialRuleDefinition Definition,
    IReadOnlyList<RuleArgument> Arguments)
{
    /// <summary> Convenience for the common no-argument case. </summary>
    public ResolvedRule(string requestedName, SpecialRuleDefinition definition)
        : this(requestedName, definition, Array.Empty<RuleArgument>())
    {
    }
}
