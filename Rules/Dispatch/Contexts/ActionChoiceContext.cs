using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Activation_OnActionChoice"/>: the engine is about to ask the activating
    /// player which action to take. Lets a rule offer the unit a custom action (a spell/ability) alongside
    /// the built-in Move/Charge/Shoot/Pass choices — the #010 custom-action seam. <see cref="Unit"/> is the
    /// activating unit; the offer addresses it via <see cref="IHasActingUnit"/>.
    /// </summary>
    public sealed record ActionChoiceContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Activation_OnActionChoice;

        public IUnit ActingUnit => Unit;
    }
}
