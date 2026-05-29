using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Melee_OnMeleeResolution"/>: wounds are tallied on
    /// both sides, before morale. Trigger for Fear's "+X wounds dealt for the
    /// winner-check."
    ///
    /// Minimal for now (the two combatants); likely to grow the per-side wound counts
    /// when a rule needs to read them.
    /// </summary>
    public sealed record MeleeResolutionContext(IUnit Attacker, IUnit Defender) : IHookContext
    {
        public EHookID Hook => EHookID.Melee_OnMeleeResolution;
    }
}
