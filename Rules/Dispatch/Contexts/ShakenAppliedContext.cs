using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// #197 P17c — the bearer just became Shaken, fired by the morale path
    /// (<c>MoraleUtilities.ApplyShakenWithPresentation</c>). The hook existed only as a
    /// <c>TokenClearTrigger.CustomHook</c> target before (Fortified's lose-all-markers sweep); this
    /// context makes it an evaluable moment, which is what Reinforcement's "when a unit ... is
    /// Shaken ... you may remove it from the table as destroyed" arm hangs on.
    /// </summary>
    public sealed record ShakenAppliedContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Morale_OnShakenApplied;

        public IUnit ActingUnit => Unit;
    }
}
