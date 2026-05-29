using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Activation_OnPreAttack"/>: the unit has chosen an
    /// attack action, before targets and weapons resolve. The trigger point at which
    /// activated abilities (Mend, Re-Position Artillery, Unstoppable Mark) are offered.
    /// </summary>
    public sealed record PreAttackContext(IUnit Unit, EActionType ActionType) : IHookContext
    {
        public EHookID Hook => EHookID.Activation_OnPreAttack;
    }
}
