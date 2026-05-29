using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnPostShoot"/>: the shoot action is
    /// complete. Trigger for "after shooting" bookkeeping — Limited marks the weapon
    /// used here so a later use is suppressed.
    /// </summary>
    public sealed record PostShootContext(IUnit Attacker, IUnit Target) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnPostShoot;
    }
}
