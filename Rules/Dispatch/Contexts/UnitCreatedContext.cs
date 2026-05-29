using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Lifecycle_OnUnitCreated"/>: a unit was first added to
    /// the table at army-setup time. Used for creation-time bookkeeping and for auras
    /// that grant a rule to the bearer's whole unit (Regeneration Aura).
    /// </summary>
    public sealed record UnitCreatedContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Lifecycle_OnUnitCreated;
    }
}
