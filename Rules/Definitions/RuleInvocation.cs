using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// One firing of one rule instance: the world event (<see cref="Hook"/>) plus who
/// the rule is attached to (<see cref="Bearer"/>) and that attachment's arguments
/// (<see cref="Arguments"/>, e.g. Deadly(3)). The hook context can't carry the last
/// two because the same event is evaluated for several units/seats — they vary per
/// bearer, which is what RuleEvaluator knows as it walks a unit's rules.
/// </summary>
public sealed record RuleInvocation(IHookContext Hook, IUnit Bearer, IReadOnlyList<RuleArgument> Arguments);