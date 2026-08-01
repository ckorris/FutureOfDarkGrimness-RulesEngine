using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Capability for <see cref="EHookID.Lifecycle_OnWoundIgnored"/>: how many wounds the bearer just shrugged
/// off. #197 P12 Regenerative Strength ("place one marker on this model when it ignores a wound") is the
/// only reader — the marker's value IS this number.
///
/// <para><b>Float, and it has to be.</b> Every other numeric an effect can reach is an <c>int</c>, because
/// every other one is authored (<see cref="ValueSource.Literal"/>, <see cref="ValueSource.Arg"/>) or
/// counted off the table (<see cref="ValueSource.RuleCarrierCount"/>). This one is roll-derived: the wound
/// pool arriving at the ignore fold is already fractional under the probabilistic roller, and the ignore
/// roll spreads it across faces again. Exposing it as an int would round a roll-derived value at the seam
/// - precisely what the dice invariant forbids - so the capability is typed honestly and the marker
/// carries a <see cref="Tokens.TokenPayload.Magnitude"/>. Under the realistic roller it is always whole.</para>
///
/// <para>Lives in Definitions rather than Foundation because the context implementing it references
/// <see cref="IUnit"/>, same rationale as <see cref="IHasKillerUnit"/>.</para>
/// </summary>
public interface IHasIgnoredWoundCount : ICapability
{
    /// <summary>
    /// Wounds ignored by this firing's bearer. Always greater than zero — the fire site skips the hook
    /// entirely when nothing was ignored, so a rule here never has to guard against a no-op firing.
    /// </summary>
    float IgnoredWoundCount { get; }
}
