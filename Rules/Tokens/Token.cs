using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

/// <summary>
/// A single piece of rule-related state attached to an <see cref="IUnit"/> or
/// <see cref="IModel"/>. Tokens are the unifying state primitive for the
/// data-driven special-rules system — cost gates, status conditions, stacking
/// markers, "next time" buffs, cross-unit target tags, and per-activation effects
/// are all expressed as tokens with appropriate <see cref="ClearTrigger"/> values.
///
/// Pure data: no callbacks, no engine references, trivially serializable. The
/// engine's token-clear service walks each container at relevant hooks and removes
/// tokens whose <see cref="ClearTrigger"/> matches the firing hook.
///
/// Stacking semantics live in the container: tokens with the same
/// <see cref="Type"/> + <see cref="OwnerUnitID"/> increment <see cref="Count"/>;
/// different owners are kept as separate entries (so two players' marks on the
/// same enemy don't merge).
/// </summary>
/// <param name="Type">
/// What kind of token this is. Drives equality, stacking, and lookup. Engine-known
/// types (Shaken, Fatigued, SpellToken) are constants on <see cref="TokenType"/>;
/// rule data may introduce arbitrary string-identified types.
/// </param>
/// <param name="Count">
/// How many of this token are stacked at this owner. Singletons (Shaken, Fatigued)
/// cap at 1; stacking markers (Piercing Frenzy, Regenerative Strength) accumulate
/// up to the rule's defined cap. Effects that read the token use the count as a
/// numeric modifier (e.g. AP+Count, +Count extra attacks).
/// </param>
/// <param name="ClearTrigger">
/// When the engine should auto-clear this token. Required and non-nullable —
/// every token must declare its lifecycle explicitly. Use
/// <see cref="TokenClearTrigger.ManualOnly"/> for tokens whose removal is the
/// consuming rule's responsibility.
/// </param>
/// <param name="Payload">
/// Optional structured data for tokens whose <see cref="Type"/> and
/// <see cref="Count"/> alone aren't enough — most commonly spell-applied
/// "next time" rule grants that need to record which rule. Null for the majority
/// of tokens. See <see cref="TokenPayload"/> for the supported shapes.
/// </param>
/// <param name="OwnerUnitID">
/// The unit that placed this token, if it was placed by something other than the
/// bearer (cross-unit tokens like Unstoppable Mark). Null means the token is
/// self-owned — placed by the bearer's own rules. Used by the
/// <see cref="TokenClearTrigger.OwnerDestroyed"/> cleanup invariant to remove
/// cross-unit tokens when their source unit dies.
/// </param>
/// <param name="CreatedAtHook">
/// Debug-only metadata: which hook fired when this token was created. Not
/// consulted by gameplay logic; helps when tracing why a token exists in a
/// log or save inspection. Null if unspecified.
/// </param>
public record Token(TokenType Type, int Count,  TokenClearTrigger ClearTrigger, TokenPayload? Payload = null,
    UnitID? OwnerUnitID = null, EHookID? CreatedAtHook = null);
