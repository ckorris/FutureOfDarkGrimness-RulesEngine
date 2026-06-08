using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Activation_OnNextActivatorRequested"/>: the engine is about to ask a
    /// player which unit to activate next. Evaluated once per already-activated unit of the active
    /// player, so a rule like Martial Prowess can offer that unit a second activation before the normal
    /// pick. <see cref="Unit"/> is the prospective re-activator — the acting unit the offer addresses.
    /// </summary>
    public sealed record NextActivatorRequestedContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Activation_OnNextActivatorRequested;

        public IUnit ActingUnit => Unit;
    }
}
