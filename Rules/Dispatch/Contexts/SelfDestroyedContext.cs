using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// #197 P17 — the bearer was just fully destroyed, fired by <c>UnitDestructionNotifier</c> for every
    /// alive-to-dead transition (killer or not — a rout still counts, which is why this is not
    /// <see cref="UnitDestroyedContext"/>, the killer-seat context that requires one). The offer site
    /// for Split's "when this unit is fully destroyed, you may place a new unit of X fully within 6\"
    /// of it before removing the last model".
    /// </summary>
    public sealed record SelfDestroyedContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Lifecycle_OnSelfDestroyed;

        public IUnit ActingUnit => Unit;
    }
}
