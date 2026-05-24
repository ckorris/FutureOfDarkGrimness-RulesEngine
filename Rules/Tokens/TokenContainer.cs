using System.Diagnostics;
using FDG.Rules.Foundation;
using Newtonsoft.Json;

namespace FDG.Rules.Tokens;

public class TokenContainer : ITokenContainer
{
    [JsonProperty] private List<Token> _tokens = new();
    
    public event Action<Token>? OnTokenAdded;
    public event Action<Token>? OnTokenRemoved;
    public event Action<Token>? OnTokenCountChanged;
    
    public void AddToken(Token token)
    {
        if (token.Count <= 0)
        {
            Debug.WriteLine($"Tried to add a token of type {token.Type} with a count of 0.");
            return;
        }
        
        //If we already have tokens of the same type and owner, just add to that pile.
        //Note this assumes that there is nothing else different about the input tokens.
        int existingIndex = _tokens.FindIndex(t => t.Type == token.Type && t.OwnerUnitID == token.OwnerUnitID);
        if (existingIndex < 0)
        {
            _tokens.Add(token);
            OnTokenAdded?.Invoke(token);
            return;
        }

        Token existing = _tokens[existingIndex];
        Token updated = existing with { Count = existing.Count + token.Count };
        _tokens[existingIndex] = updated;
        OnTokenCountChanged?.Invoke(updated);

    }

    public int RemoveTokens(TokenType tokenType, int count = 1)
    {
        if (count <= 0)
        {
            return 0; //lolwut
        }
        
        int totalRemoved = 0;
        for (int i = 0; i < _tokens.Count && totalRemoved < count;)
        {
            Token entry =  _tokens[i];
            if (entry.Type != tokenType)
            {
                i++;
                continue;
            }

            int remaining = count - totalRemoved;
            int takeFromEntry = Math.Min(remaining, entry.Count);
            int newCount = entry.Count - takeFromEntry;
            totalRemoved += takeFromEntry;

            if (newCount == 0) //Drained all tokens from this.
            {
                _tokens.RemoveAt(i);
                OnTokenRemoved?.Invoke(entry);
            }
            else
            {
                Token updated = entry with { Count = newCount };
                _tokens[i] = updated;
                OnTokenCountChanged?.Invoke(updated);
                i++;
            }
        }

        return totalRemoved;
    }

    public bool HasToken(TokenType tokenType)
    {
        return _tokens.Any(token => token.Type == tokenType);
    }

    public int GetTokenCount(TokenType tokenType)
    {
        int total = 0;
        foreach (Token token in _tokens.Where(token => token.Type == tokenType))
        {
            total += token.Count;
        }

        return total;
    }

    public IEnumerable<Token> GetAllTokens(TokenType? tokenType = null)
    {
        if (tokenType == null)
        {
            return _tokens;
        }
        else
        {
            return _tokens.Where(token => token.Type == tokenType);
        }
    }

    public IEnumerable<Token> TokensWithOwner(UnitID owningUnitID)
    {
        return _tokens.Where(token => token.OwnerUnitID == owningUnitID);
    }


}