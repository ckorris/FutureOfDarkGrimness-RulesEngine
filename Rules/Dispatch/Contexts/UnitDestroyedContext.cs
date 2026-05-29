using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnUnitDestroyed"/>: a unit's last model
    /// died, attributed to a killer. Trigger for "destroyed an enemy" effects (Piercing
    /// Frenzy markers) and for owner-destroyed token cleanup (Unstoppable Mark): the
    /// <see cref="DestroyedUnit"/> may be the owner of cross-unit tokens that must clear.
    /// </summary>
    public sealed record UnitDestroyedContext(IUnit DestroyedUnit, IUnit KillerUnit) : IHookContext
    {
        public EHookID Hook => EHookID.Shooting_OnUnitDestroyed;
    }
}
