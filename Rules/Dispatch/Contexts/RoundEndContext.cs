using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Round_OnRoundEnd"/>: the bottom of a round, after every
    /// activation and before the round-end token sweep (so tokens that clear at round end
    /// are still visible to the rules evaluated here). Carries the bearer whose round-end
    /// rules are evaluated — Fortified Growth accumulates its marker here (#100 #13).
    /// Mirror of <see cref="RoundStartContext"/>.
    /// </summary>
    public sealed record RoundEndContext(IUnit Unit) : IHookContext
    {
        public EHookID Hook => EHookID.Round_OnRoundEnd;
    }
}
