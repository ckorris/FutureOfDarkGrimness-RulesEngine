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
    
    public IEnumerable<Token> GetAllTokens(TokenType? tokenType = null);
    
    public IEnumerable<Token> TokensWithOwner(UnitID owningUnitID);
    
    public event Action<Token>? OnTokenAdded;
    
    public event Action<Token>? OnTokenRemoved;

    public event Action<Token>? OnTokenCountChanged;
}