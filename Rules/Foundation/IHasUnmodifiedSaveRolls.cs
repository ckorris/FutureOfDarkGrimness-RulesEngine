namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hook context that carries the unmodified SAVE-roll outcomes (the
/// defender's block dice). The save-side mirror of <see cref="IHasUnmodifiedHitRolls"/>, so
/// "on an unmodified N to block" rules — Shred (+1 wound on a natural 1) — read counts via
/// <c>UnmodifiedSaveRolls.At(1)</c> rather than scanning dice, and stay correct under both the
/// realistic roller (integer counts) and the probabilistic roller (fractional expected counts).
/// </summary>
public interface IHasUnmodifiedSaveRolls : ICapability
{
    public IDiceResults UnmodifiedSaveRolls { get; }
}
