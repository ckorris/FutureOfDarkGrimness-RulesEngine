using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// The result of looking up a rule by name via <see cref="IRuleResolver"/>. Pairs
/// the <see cref="SpecialRuleDefinition"/> that was found with the
/// <see cref="RequestedName"/> the caller used to find it — which may be an alias
/// rather than the definition's canonical <see cref="SpecialRuleDefinition.Name"/>.
///
/// The two fields serve distinct roles:
/// <list type="bullet">
///   <item><see cref="Definition"/> is used for identity comparisons. For example,
///         <c>Effect.IgnoreRule("Regeneration")</c> matches any attached rule whose
///         <see cref="Definition"/> is the Regeneration definition, including units
///         that authored it under the alias "Healing Pods".</item>
///   <item><see cref="RequestedName"/> is used for display. UI can show
///         "Healing Pods (Regeneration)" when the requested name differs from the
///         canonical one.</item>
/// </list>
/// </summary>
public record ResolvedRule(string RequestedName, SpecialRuleDefinition Definition);
