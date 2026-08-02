using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

public interface ITokenContainer
{
    public void AddToken(Token token);
    
    public int RemoveTokens(TokenType tokenType, int count = 1);

    /// <summary>
    /// Removes up to <paramref name="count"/> tokens of <paramref name="tokenType"/>
    /// whose <c>OwnerUnitID</c> equals <paramref name="owner"/>, leaving same-type
    /// tokens placed by other owners untouched. The owner-scoped counterpart to
    /// <see cref="RemoveTokens"/> (which is owner-agnostic) — used by owner-destroyed
    /// cleanup so one placer's cross-unit marks clear without disturbing another's.
    /// Returns the number actually removed.
    /// </summary>
    public int RemoveTokensWithOwner(TokenType tokenType, UnitID owner, int count = 1);

    /// <summary>
    /// Removes up to <paramref name="count"/> tokens matching <paramref name="tokenType"/>,
    /// <paramref name="owner"/> (nullable — matches the no-owner pile) AND <paramref name="payload"/>
    /// (value equality), leaving same-type tokens with a different owner or payload untouched. The
    /// fully-precise counterpart used to consume one specific granted rule (a <c>RuleGrant</c> token) or
    /// clear one expired entry without disturbing the unit's other grants. Returns the number removed.
    /// </summary>
    public int RemoveTokensWithPayload(TokenType tokenType, UnitID? owner, TokenPayload? payload, int count = 1);

    /// <summary>
    /// Removes up to <paramref name="count"/> tokens of <paramref name="tokenType"/> whose clear trigger
    /// is <see cref="TokenClearTrigger.FirstTrigger"/>, leaving duration-triggered entries of the same
    /// type untouched. Used by one-shot grant consumption (<c>GrantedRollModifiers.ConsumeNet</c>) so
    /// spending a "next roll" buff can never drain a coexisting "this round" buff of the same roll kind.
    /// Returns the number actually removed.
    /// </summary>
    public int RemoveFirstTriggerTokens(TokenType tokenType, int count = 1);

    public bool HasToken(TokenType tokenType);

    public int GetTokenCount(TokenType tokenType);

    /// <summary>
    /// #197 P12: the summed <see cref="TokenPayload.Magnitude"/> of every token of
    /// <paramref name="tokenType"/> — the fractional counterpart to <see cref="GetTokenCount"/>, and the
    /// only place in the engine where a token's value is allowed to be a non-integer. Entries without a
    /// magnitude payload contribute nothing, so a caller that asks the wrong type gets 0 rather than a
    /// silently wrong integer count. Returns 0 when the bearer has no such tokens.
    /// </summary>
    public float GetTokenMagnitude(TokenType tokenType);


    /// <summary>
    /// Every token on this container, optionally filtered to <paramref name="tokenType"/>.
    /// <para><b>Implementations must return a SNAPSHOT</b> (#326) — never the backing collection and
    /// never a lazy view over it. Tokens are written on the engine thread and read on the render thread
    /// every frame, so a live result let the renderer enumerate mid-mutation and threw "Collection was
    /// modified" out of the draw loop. A caller cannot defend itself here: copying with <c>.ToList()</c>
    /// at the call site has to enumerate the live collection to make the copy.</para>
    /// </summary>
    public IEnumerable<Token> GetAllTokens(TokenType? tokenType = null);

    /// <summary>
    /// Every token placed by <paramref name="owningUnitID"/>. A snapshot, for the same reason as
    /// <see cref="GetAllTokens"/> (#326).
    /// </summary>
    public IEnumerable<Token> TokensWithOwner(UnitID owningUnitID);
    
    public event Action<Token>? OnTokenAdded;
    
    public event Action<Token>? OnTokenRemoved;

    public event Action<Token>? OnTokenCountChanged;
}