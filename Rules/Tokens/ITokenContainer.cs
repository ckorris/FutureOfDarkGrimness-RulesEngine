using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

public interface ITokenContainer
{
    public void AddToken(Token token);
    
    public int RemoveTokens(TokenType tokenType, int count = 1);
    
    public bool HasToken(TokenType tokenType);

    public int GetTokenCount(TokenType tokenType);
    
    public IEnumerable<Token> GetAllTokens(TokenType? tokenType = null);
    
    public IEnumerable<Token> TokensWithOwner(UnitID owningUnitID);
    
    public event Action<Token>? OnTokenAdded;
    
    public event Action<Token>? OnTokenRemoved;

    public event Action<Token>? OnTokenCountChanged;
}