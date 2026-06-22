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
        
        // Merge into an existing pile only when type, owner AND payload all match. Payload is part of the
        // identity (#101): two RuleGrant tokens naming different rules — or two StatModifier grants with
        // different deltas — from the same owner must stay distinct entries, not collapse into one pile that
        // silently keeps only the first's payload. Tokens with no payload (the common case) compare equal
        // under Equals(null, null) and merge exactly as before.
        int existingIndex = _tokens.FindIndex(t => t.Type == token.Type && t.OwnerUnitID == token.OwnerUnitID
            && Equals(t.Payload, token.Payload));
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
        => RemoveMatching(entry => entry.Type == tokenType, count);

    public int RemoveTokensWithOwner(TokenType tokenType, UnitID owner, int count = 1)
        => RemoveMatching(entry => entry.Type == tokenType && entry.OwnerUnitID == owner, count);

    public int RemoveTokensWithPayload(TokenType tokenType, UnitID? owner, TokenPayload? payload, int count = 1)
        => RemoveMatching(entry => entry.Type == tokenType && entry.OwnerUnitID == owner
            && Equals(entry.Payload, payload), count);

    /// <summary>
    /// Drains up to <paramref name="count"/> tokens from entries satisfying
    /// <paramref name="match"/>, in insertion order, firing OnTokenRemoved when an entry
    /// hits zero and OnTokenCountChanged on a partial draw. Shared by the owner-agnostic
    /// and owner-scoped public removers — they differ only in the predicate.
    /// </summary>
    private int RemoveMatching(Func<Token, bool> match, int count)
    {
        if (count <= 0)
        {
            return 0; //lolwut
        }

        int totalRemoved = 0;
        for (int i = 0; i < _tokens.Count && totalRemoved < count;)
        {
            Token entry =  _tokens[i];
            if (!match(entry))
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