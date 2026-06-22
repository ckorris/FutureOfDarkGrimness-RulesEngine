using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Shooting_OnPreApplyWound"/>: each wound is about to
    /// be assigned to a model. Trigger for wound-multiplication (Deadly) and
    /// wound-ignore (Regeneration). Carries the attack's source and target units;
    /// per-model allocation detail is added when a test needs it.
    /// </summary>
    public sealed record PreApplyWoundContext(IUnit Attacker, IUnit Defender) : IHookContext, IHasTarget
    {
        public EHookID Hook => EHookID.Shooting_OnPreApplyWound;

        // The defender IS the target for target-keyed conditions.
        IUnit IHasTarget.Target => Defender;
    }
}
