using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Round_OnRoundStart"/>: the top of a round, before
    /// any activations. Carries the bearer whose round-start rules are evaluated
    /// (Caster replenishes spell tokens here).
    ///
    /// Minimal for now (the bearer); likely to grow the round number when a rule
    /// needs to gate on "after the first round" (e.g. Ambush timing).
    /// </summary>
    public sealed record RoundStartContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Round_OnRoundStart;
    }
}
