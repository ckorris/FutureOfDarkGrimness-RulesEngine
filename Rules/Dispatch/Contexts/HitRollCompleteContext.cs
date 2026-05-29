using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnHitRollComplete"/>: hit rolls are
    /// collected, before the save flow. Carries the unmodified hit-roll values so
    /// natural-6 rules (Furious, Rending, Surge) can react, plus the shooting
    /// distance for range-gated ones (Relentless &gt; 9").
    ///
    /// Fields still grow on demand — likely future additions: the weapon and its AP
    /// (Rending promotes AP on a 6), and an is-melee flag once melee hit rolls share
    /// this hook.
    /// </summary>
    public sealed record HitRollCompleteContext(
        IUnit Attacker, IUnit Target, IReadOnlyList<int> UnmodifiedHitRolls,
        float DistanceInches = 0f) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnHitRollComplete;
    }
}
