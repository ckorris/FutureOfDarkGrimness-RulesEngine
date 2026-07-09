using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Activation_OnActivationStart"/>: the unit's activation has begun and no
    /// action has been chosen yet. Carries only the activating unit — the choices this hook exists for are
    /// about the unit itself, not about any target.
    ///
    /// The offer site for the corpus' "when this unit is activated, pick one effect ... until the end of the
    /// activation" rules (Versatile Attack, Watchborn, Versatile Reach). Each such rule contributes one
    /// <see cref="ActivatedAbility"/> per effect at this hook; <c>ActivationStartStage</c> asks the player
    /// which, and the chosen ability grants a helper rule for the activation via
    /// <see cref="Effect.AddRule"/> with <see cref="ELifetime.ThisActivation"/> — the use that effect's own
    /// documentation names.
    /// </summary>
    public sealed record ActivationStartContext(IUnit Unit) : IHookContext, IHasActingUnit
    {
        public EHookID Hook => EHookID.Activation_OnActivationStart;

        public IUnit ActingUnit => Unit;
    }
}
