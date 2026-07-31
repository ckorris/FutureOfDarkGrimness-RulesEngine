using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnUnitDestroyed"/>: a unit's last model
    /// died, attributed to a killer. Trigger for "destroyed an enemy" effects (Piercing
    /// Frenzy markers) and for owner-destroyed token cleanup (Unstoppable Mark): the
    /// <see cref="DestroyedUnit"/> may be the owner of cross-unit tokens that must clear.
    ///
    /// <para>Carries <see cref="IHasKillerUnit"/> (#197 Vengeance) so the mirror case works too — a rule
    /// on the DEAD unit acting on its killer, from the <see cref="ERuleSeat.Subject"/> seat.</para>
    /// </summary>
    public sealed record UnitDestroyedContext(IUnit DestroyedUnit, IUnit KillerUnit)
        : IHookContext, IHasKillerUnit
    {
        public EHookID Hook => EHookID.Shooting_OnUnitDestroyed;
    }
}
