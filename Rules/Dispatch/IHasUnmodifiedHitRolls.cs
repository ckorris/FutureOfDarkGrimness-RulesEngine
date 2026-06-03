namespace FDG.Rules.Dispatch;

/// <summary>
/// Capability interface: a hook context that carries the unmodified hit-roll
/// outcomes. Exposed as <see cref="IDiceResults"/> — the engine's per-face
/// histogram, not a per-die list — so natural-N rules read counts via
/// <c>UnmodifiedHitRolls.At(6)</c> rather than scanning dice. This is what lets
/// the rule system work identically under the realistic roller (integer counts)
/// and the <c>ProbabilisticDiceRoller</c> (fractional expected counts, e.g.
/// At(6) == rolls / 6).
/// </summary>
public interface IHasUnmodifiedHitRolls
{
    public IDiceResults UnmodifiedHitRolls { get; }
}