namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hook context that knows whether the hits being resolved come from a spell
/// (vs. a weapon attack). Lets <see cref="Rules.Definitions.Condition.IsNotSpell"/> gate rules whose
/// corpus text excludes spell damage (Shielded's "against hits that are NOT from spells") on hooks the
/// spell-damage pipeline shares with the weapon pipelines (Shooting_OnHitRollComplete).
/// </summary>
public interface IHasIsSpell : ICapability
{
    bool IsSpell { get; }
}
