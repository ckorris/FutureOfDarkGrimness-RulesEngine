using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// #197 P22 — the moment a unit's activation ends, fired by <c>ReconcileEndOfActivationStage</c>
    /// BEFORE its token sweep. The end-of-activation twin of <see cref="ActivationStartContext"/>:
    /// activated abilities the corpus hangs on "when this unit ends its activation" are offered here
    /// (Ambush Re-Deployment's self-removal; Dash's end-of-activation reposition when it lands).
    /// </summary>
    public sealed record ActivationEndContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Activation_OnEndOfActivation;

        public IUnit ActingUnit => Unit;
    }
}
