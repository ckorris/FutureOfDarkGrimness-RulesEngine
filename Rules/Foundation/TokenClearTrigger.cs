using System.Text.Json.Serialization;
using FDG.Rules.Tokens;

namespace FDG.Rules.Foundation;

/// <summary>
/// Discriminated union describing when a <see cref="Token"/> should be auto-cleared from
/// its container. Stored as a field on each token; consumed by the engine's token-clear
/// service, which walks containers at relevant hooks and removes tokens whose trigger matches.
///
/// Implemented as an abstract record with sealed nested record subtypes (the canonical
/// modern C# pattern for closed sum types). Records give value equality for free
/// (<c>new RoundEnd() == new RoundEnd()</c> is true), <c>sealed</c> on each variant
/// prevents accidental further subclassing, and pattern matching in switch expressions
/// reads naturally:
///
/// <code>
/// bool shouldClear = trigger switch
/// {
///     TokenClearTrigger.RoundEnd                     => firedHook == EHookID.Round_OnRoundEnd,
///     TokenClearTrigger.CustomHook(var hook)         => firedHook == hook,
///     _                                              => false,
/// };
/// </code>
///
/// Always reference the base type as the field type; construct concrete variants
/// (e.g. <c>new TokenClearTrigger.RoundEnd()</c>) at the point of use.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ManualOnly), "manualOnly")]
[JsonDerivedType(typeof(RoundEnd), "roundEnd")]
[JsonDerivedType(typeof(ActivationEnd), "activationEnd")]
[JsonDerivedType(typeof(AttackEnd), "attackEnd")]
[JsonDerivedType(typeof(FirstTrigger), "firstTrigger")]
[JsonDerivedType(typeof(UnitDestroyed), "unitDestroyed")]
[JsonDerivedType(typeof(OwnerDestroyed), "ownerDestroyed")]
[JsonDerivedType(typeof(CustomHook), "customHook")]

public abstract record TokenClearTrigger
{
    /// <summary>
    /// Never auto-clears. The consuming rule must remove it explicitly. Used for
    /// permanent buffs and "next time it would apply" tokens whose consumption is
    /// the responsibility of the rule that reads them.
    /// </summary>
    public sealed record ManualOnly : TokenClearTrigger;

    /// <summary>
    /// Clears at <see cref="EHookID.Round_OnRoundEnd"/>. Used for per-round bookkeeping
    /// such as Fatigue and per-round cost gates.
    /// </summary>
    public sealed record RoundEnd : TokenClearTrigger;

    /// <summary>
    /// Clears at <see cref="EHookID.Activation_OnEndOfActivation"/> for the bearer's unit.
    /// Used for per-activation effects and once-per-activation cost gates
    /// (e.g. Versatile Attack/Reach buffs).
    /// </summary>
    public sealed record ActivationEnd : TokenClearTrigger;

    /// <summary>
    /// Clears at <see cref="EHookID.Shooting_OnPostShoot"/> or
    /// <see cref="EHookID.Melee_OnPostMelee"/> for the bearer. Used for
    /// "this attack only" modifiers.
    /// </summary>
    public sealed record AttackEnd : TokenClearTrigger;

    /// <summary>
    /// Auto-decrements the token's count when the rule that reads it fires. Used for
    /// spell-applied "ignored next wound" and "+1 to hit next time" buffs that the
    /// consuming rule doesn't have to manually clean up.
    /// </summary>
    public sealed record FirstTrigger : TokenClearTrigger;

    /// <summary>
    /// Cleared when the bearer's unit is destroyed. Usually automatic via unit teardown;
    /// declared explicitly for tokens whose lifetime is "exists until I die" with no
    /// other condition.
    /// </summary>
    public sealed record UnitDestroyed : TokenClearTrigger;

    /// <summary>
    /// Cleared when the token's <c>OwnerUnitId</c> unit is destroyed (the unit that
    /// placed the token, not the unit carrying it). Used for cross-unit tokens like
    /// Unstoppable Mark, so the mark disappears if its source dies.
    /// </summary>
    public sealed record OwnerDestroyed : TokenClearTrigger;

    /// <summary>
    /// Clears when the named hook fires. Escape hatch for edge cases not covered by
    /// the other triggers (e.g. "clears at next <see cref="EHookID.Round_OnRoundStart"/>").
    /// </summary>
    public sealed record CustomHook(EHookID Hook) : TokenClearTrigger;
}
