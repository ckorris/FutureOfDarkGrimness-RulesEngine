using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnPostShoot"/> once per shoot ACTION — the unit has
    /// finished shooting (after the last weapon, or when no further valid shots remain). The trigger
    /// for "after this unit shoots, it may move" repositioning: Hit & Run / Harassing's optional move.
    ///
    /// Distinct from the dormant per-shot <see cref="PostShootContext"/> (attacker+target, shaped for
    /// Limited's per-weapon bookkeeping): a unit firing several weapons at several targets reaches THIS
    /// hook exactly once, so a post-shoot move is offered once rather than once per shot. Carries only
    /// the acting unit — the rules at this hook are self-targeted (they move the shooter).
    /// </summary>
    public sealed record PostShootActionContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnPostShoot;
    }
}
