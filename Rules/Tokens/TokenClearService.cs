using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

/// <summary>
/// Clears tokens whose lifetime has ended, walking token containers when the
/// triggering event fires. This is the half of the token system that the
/// declarative <see cref="TokenClearTrigger"/> data describes but doesn't execute:
/// a token says <i>when</i> it should clear, and this service does the clearing.
///
/// It is deliberately event- and store-agnostic — callers hand it the containers to
/// walk and the identity involved, so the same logic serves the test harness (walking
/// the data store) and, later, a real stage (walking <c>ITableState</c>) when a unit is
/// destroyed. Cross-unit ownership lives on the <i>target</i>'s tokens, so cleanup must
/// sweep every container, not just the dead unit's own.
/// </summary>
public sealed class TokenClearService
{
    /// <summary>
    /// Removes the cross-unit tokens that <paramref name="destroyedOwner"/> placed and
    /// that declared <see cref="TokenClearTrigger.OwnerDestroyed"/> — the marks (e.g.
    /// Unstoppable Mark) that exist only while their placing unit lives. Tokens the dead
    /// unit owns under a different clear trigger are left alone; their lifetime is whatever
    /// that trigger says, not "until the placer dies."
    /// </summary>
    /// <summary>
    /// Removes, from each of <paramref name="containers"/>, the tokens whose
    /// <see cref="TokenClearTrigger"/> fires at <paramref name="firedHook"/> — the lifecycle half of
    /// the once-per-X cost gates (e.g. an <see cref="TokenClearTrigger.ActivationEnd"/> "used this
    /// activation" marker clears at <see cref="EHookID.Activation_OnEndOfActivation"/>). Owner-stamped
    /// tokens are removed owner-scoped so one placer's mark doesn't drag another's of the same type.
    /// </summary>
    public void ClearForHook(EHookID firedHook, IEnumerable<ITokenContainer> containers)
    {
        foreach (ITokenContainer container in containers)
        {
            // Snapshot first — removal mutates the container we're reading.
            List<Token> expired = container.GetAllTokens()
                .Where(token => ClearsAtHook(token.ClearTrigger, firedHook))
                .ToList();

            foreach (Token token in expired)
            {
                // Payload-precise (#101): with several distinct payload-bearing tokens sharing a type+owner
                // (e.g. two different granted rules on one unit), clear only the expired entry, not whichever
                // same-type token happens to come first. No-payload tokens match under Equals(null, null).
                container.RemoveTokensWithPayload(token.Type, token.OwnerUnitID, token.Payload, token.Count);
            }
        }
    }

    private static bool ClearsAtHook(TokenClearTrigger trigger, EHookID firedHook) => trigger switch
    {
        TokenClearTrigger.ActivationEnd => firedHook == EHookID.Activation_OnEndOfActivation,
        TokenClearTrigger.RoundEnd => firedHook == EHookID.Round_OnRoundEnd,
        TokenClearTrigger.CustomHook custom => firedHook == custom.Hook,
        _ => false,
    };

    public void ClearForDestroyedOwner(UnitID destroyedOwner, IEnumerable<ITokenContainer> containers)
    {
        foreach (ITokenContainer container in containers)
        {
            // Snapshot first — removal mutates the container we're reading.
            List<Token> ownerDestroyedMarks = container
                .TokensWithOwner(destroyedOwner)
                .Where(token => token.ClearTrigger is TokenClearTrigger.OwnerDestroyed)
                .ToList();

            foreach (Token mark in ownerDestroyedMarks)
            {
                container.RemoveTokensWithOwner(mark.Type, destroyedOwner, mark.Count);
            }
        }
    }
}
