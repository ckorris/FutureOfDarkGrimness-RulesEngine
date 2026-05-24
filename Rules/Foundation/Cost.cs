namespace FDG.Rules.Foundation;

/// <summary>
/// Discriminated union describing the cost an activated ability or spell pays when used.
/// Stored on an <c>ActivatedAbility</c>; consulted by the engine to decide whether the
/// ability is currently available, and to deduct the appropriate resource (tokens,
/// once-per-X gates) when the ability fires.
///
/// Implemented as an abstract record with sealed nested record subtypes — same pattern as
/// <see cref="TokenClearTrigger"/>. Variants are compared by value, pattern-match cleanly
/// in switch expressions, and are closed against external subclassing.
///
/// <code>
/// bool isAffordable = cost switch
/// {
///     Cost.OncePerActivation     => !unit.Tokens.Has(usedThisActivationMarker),
///     Cost.SpellTokens(var n)    => unit.Tokens.Count(TokenType.SpellToken) >= n,
///     Cost.ConsumesToken(var t, var n) => unit.Tokens.Count(t) >= n,
///     _                          => true,
/// };
/// </code>
///
/// In practice the once-per-X variants are implemented under the hood as token grants
/// and consumptions (see <see cref="TokenClearTrigger"/>) — they exist as first-class
/// Cost variants so authoring stays readable.
/// </summary>
public abstract record Cost
{
    /// <summary>
    /// The ability may be used at most once during the bearer's current activation.
    /// Implemented as a "used-this-activation" token granted on use and cleared at
    /// <see cref="EHookID.Activation_OnEndOfActivation"/>.
    /// </summary>
    public sealed record OncePerActivation : Cost;

    /// <summary>
    /// The ability may be used at most once during the current round. Implemented as
    /// a "used-this-round" token granted on use and cleared at
    /// <see cref="EHookID.Round_OnRoundEnd"/>.
    /// </summary>
    public sealed record OncePerRound : Cost;

    /// <summary>
    /// The ability may be used at most once per game. Implemented as a one-shot token
    /// granted at <see cref="EHookID.Lifecycle_OnUnitCreated"/> and consumed on use.
    /// </summary>
    public sealed record OncePerGame : Cost;

    /// <summary>
    /// Spends <see cref="Count"/> spell tokens from the bearer's <see cref="TokenType.SpellToken"/>
    /// pool. Used by Caster(X) spells.
    /// </summary>
    public sealed record SpellTokens(int Count) : Cost;

    /// <summary>
    /// Spends <see cref="Count"/> tokens of the given <see cref="TType"/> from the
    /// bearer's container. The general-purpose "pay X of type T" cost — covers anything
    /// from spending stacking markers to consuming buff tokens.
    /// </summary>
    public sealed record ConsumesToken(TokenType TType, int Count = 1) : Cost;
}
