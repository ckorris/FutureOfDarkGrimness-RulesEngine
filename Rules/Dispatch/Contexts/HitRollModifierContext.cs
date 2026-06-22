using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnHitRollModifier"/>: adjusting Quality
    /// or per-roll modifiers before rolling to hit. Carries the attacker-to-target
    /// distance (Stealth &gt; 9"), whether the attacker moved this activation
    /// (Indirect's -1 after moving), whether the attack is melee (the hit-roll stages
    /// are shared with shooting), and whether the attacker is charging (Thrust's +1 to hit).
    /// </summary>
    public sealed record HitRollModifierContext(
        IUnit Attacker, IUnit Target, float DistanceInches, bool AttackerMoved = false,
        bool IsMelee = false, bool IsCharging = false)
        : IHookContext, IHasDistance, IHasAttackerMoved, IHasCombatKind, IHasCharging, IHasTarget
    {
        public EHookID Hook => EHookID.Shooting_OnHitRollModifier;
    }
}
