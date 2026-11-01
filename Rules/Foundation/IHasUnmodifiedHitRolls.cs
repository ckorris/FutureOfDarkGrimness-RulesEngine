namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hook context that carries the unmodified hit-roll
/// outcomes. Exposed as <see cref="IDiceResults"/> — the engine's per-face
/// histogram, not a per-die list — so natural-N rules read counts via
/// <c>SpecialRuleHitRolls.At(6)</c> rather than scanning dice. This is what lets
/// the rule system work identically under the realistic roller (integer counts)
/// and the <c>ProbabilisticDiceRoller</c> (fractional expected counts, e.g.
/// At(6) == rolls / 6).
/// </summary>
public interface IHasUnmodifiedHitRolls : ICapability
{
    /// <summary>The natural faces exactly as rolled - what the dice show.</summary>
    public IDiceResults UnmodifiedHitRolls { get; }

    /// <summary>
    /// The same rolls as SPECIAL RULES see them, which is what every "on an unmodified N to hit" trigger
    /// reads (<c>Condition.UnmodifiedRollEquals</c>, <c>Effect.AddExtraHit</c>,
    /// <c>Effect.SelfWoundOnUnmodifiedRoll</c>, <c>Effect.PerHitSaveModifier</c> and the per-hit AP split).
    /// Identical to <see cref="UnmodifiedHitRolls"/> except where a die must not count for its face: #418,
    /// Core Rules v3.6.0 Fatigue - a fatigued unit's melee hits "don't count as rolls of 6 for special
    /// rules", so its natural 6s read zero here while still being hits.
    /// </summary>
    public IDiceResults SpecialRuleHitRolls { get; }
}
