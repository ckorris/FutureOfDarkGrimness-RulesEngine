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
    /// <paramref name="owner"/> (nullable — matches the no-owner pile) AND <paramref name="payload"/>,
    /// leaving same-type tokens with a different payload or owner untouched. The payload-precise
    /// counterpart used when several distinct payload-bearing tokens (e.g. different granted rules) share a
    /// type and owner, so consuming/clearing one doesn't drain another. Returns the number actually removed.
    /// </summary>
    public int RemoveTokensWithPayload(TokenType tokenType, UnitID? owner, TokenPayload? payload, int count = 1);

    public bool HasToken(TokenType tokenType);

    public int GetTokenCount(TokenType tokenType);
    
    public IEnumerable<Token> GetAllTokens(TokenType? tokenType = null);
    
    public IEnumerable<Token> TokensWithOwner(UnitID owningUnitID);
    
    public event Action<Token>? OnTokenAdded;
    
    public event Action<Token>? OnTokenRemoved;

    public event Action<Token>? OnTokenCountChanged;
}