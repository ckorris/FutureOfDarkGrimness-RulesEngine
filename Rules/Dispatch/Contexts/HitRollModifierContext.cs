using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnHitRollModifier"/>: adjusting Quality
    /// or per-roll modifiers before rolling to hit. Carries the attacker-to-target
    /// distance (Stealth &gt; 9") and whether the attacker moved this activation
    /// (Indirect's -1 after moving).
    /// </summary>
    public sealed record HitRollModifierContext(
        IUnit Attacker, IUnit Target, float DistanceInches, bool AttackerMoved = false) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnHitRollModifier;
    }
}
