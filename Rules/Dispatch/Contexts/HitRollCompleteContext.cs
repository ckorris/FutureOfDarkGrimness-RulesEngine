using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnHitRollComplete"/>: hit rolls are
    /// collected, before the save flow. Carries the unmodified hit rolls as an
    /// <see cref="IDiceResults"/> histogram (so natural-6 rules read
    /// <c>UnmodifiedHitRolls.At(6)</c> and stay correct under both the realistic
    /// and probabilistic rollers), plus the shooting distance for range-gated ones
    /// (Relentless &gt; 9").
    ///
    /// <see cref="IsMelee"/> distinguishes the shooting and melee uses of this hook
    /// (the hit-roll stages are shared) so melee-only rules (Furious) can gate on it.
    ///
    /// Fields still grow on demand — likely future additions: the weapon and its AP
    /// (Rending promotes AP on a 6), and an is-charging flag (Furious's charge gate, #051).
    /// </summary>
    public sealed record HitRollCompleteContext(
        IUnit Attacker, IUnit Target, IDiceResults UnmodifiedHitRolls,
        float DistanceInches = 0f, bool IsMelee = false)
        : IHookContext, IHasUnmodifiedHitRolls, IHasDistance, IHasCombatKind
    {
        public EHookID Hook => EHookID.Shooting_OnHitRollComplete;
    }
}
