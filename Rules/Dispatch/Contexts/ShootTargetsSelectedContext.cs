using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnShootTargetsSelected"/>: weapons and
    /// targets are chosen, before any rolls. Last chance to react to target choice —
    /// Takedown re-scopes the attack to a single model here.
    ///
    /// Minimal for now (attacker and target unit); likely to grow the chosen weapon
    /// group / individual model reference when a rule needs them.
    /// </summary>
    public sealed record ShootTargetsSelectedContext(IUnit Attacker, IUnit Target) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnShootTargetsSelected;
    }
}
