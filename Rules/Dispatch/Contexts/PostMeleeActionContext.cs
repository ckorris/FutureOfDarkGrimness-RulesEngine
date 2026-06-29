using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Melee_OnPostMelee"/> once a melee is fully resolved (after strikes,
    /// strike-back, morale, and consolidation). The melee twin of <see cref="PostShootActionContext"/>:
    /// the trigger for "after melee, this unit may move" repositioning — Harassing's optional disengage
    /// move for a unit that was attacked in melee.
    ///
    /// Carries the single unit the post-melee rules act on (the charged/attacked unit, picked at the
    /// seam) — the rules here are self-targeted (they move that unit). One context per resolved melee.
    /// </summary>
    public sealed record PostMeleeActionContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Melee_OnPostMelee;
    }
}
