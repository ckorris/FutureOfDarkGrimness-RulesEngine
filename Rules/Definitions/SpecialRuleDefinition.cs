using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Top-level definition of one rule. The unit of authoring — typically one
/// file (and eventually one JSON record) per rule.
///
/// Bundles:
/// <list type="bullet">
///   <item><see cref="Name"/> — the canonical identifier rules reference each
///         other by (e.g. "Regeneration", "Furious"). Stable; used as the
///         lookup key in the rule registry.</item>
///   <item><see cref="Passive"/> — all the hook attachments that fire
///         automatically when their conditions match. May be empty (e.g. a
///         pure spell rule has no passive entries).</item>
///   <item><see cref="Activated"/> — all player-triggered abilities the rule
///         provides. May be empty (e.g. Furious is purely passive).</item>
///   <item><see cref="Scope"/> — whether the rule attaches to a unit or to an
///         individual weapon (#027). Defaults to <see cref="ERuleScope.Unit"/>;
///         army-load refuses to attach a rule at the wrong scope.</item>
///   <item><see cref="EngineArgumentCount"/> — how many arguments the rule takes
///         that the engine reads <em>directly</em> (not through an effect's
///         <see cref="ValueSource.Arg"/>). Almost always 0: arg-driven rules like
///         Tough(3) reference their value from an effect, so
///         <see cref="Dispatch.RuleArgumentArity"/> infers their arity. The
///         exception is a "marker-with-arg" core rule whose value is consumed by
///         engine code rather than an effect — Transport(X), whose capacity
///         <c>TransportUtilities.GetCapacity</c> reads — which would otherwise look
///         like a 0-arg rule (non-numeric in the picker, no value field). Counted
///         toward arity/numeric detection alongside effect-referenced args.</item>
/// </list>
///
/// A rule may have only Passive entries (Furious, Stealth), only Activated
/// entries (most spells), or both (e.g. Battleborn's "passive recovery roll
/// at round start" combined with an activated piece).
/// </summary>
public record SpecialRuleDefinition(string Name, IReadOnlyList<HookEntry> Passive,
    IReadOnlyList<ActivatedAbility> Activated, ERuleScope Scope = ERuleScope.Unit,
    int EngineArgumentCount = 0);
