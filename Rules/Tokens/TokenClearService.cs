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
