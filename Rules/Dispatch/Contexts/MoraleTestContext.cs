using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Morale_OnMoraleTestComplete"/>: the morale roll is
    /// done and its outcome is about to be applied. The hook the rulebook names for
    /// Fearless's reroll-as-pass-on-4+.
    ///
    /// Minimal for now (the testing unit); likely to grow the raw roll result and the
    /// test's source (melee vs. wounds) when a rule reads them.
    /// </summary>
    public sealed record MoraleTestContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Morale_OnMoraleTestComplete;
    }
}
