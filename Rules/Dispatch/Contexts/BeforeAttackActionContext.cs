using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Activation_OnBeforeAttackAction"/>: the engine is building the
    /// activation's action menu and the unit has not yet attacked. The trigger point at which
    /// "before attacking" activated abilities (Mend, Re-Position Artillery, Unstoppable Mark) are offered
    /// as menu actions - so they can be used even when the unit cannot attack anything.
    /// </summary>
    public sealed record BeforeAttackActionContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Activation_OnBeforeAttackAction;

        public IUnit ActingUnit => Unit;
    }
}
