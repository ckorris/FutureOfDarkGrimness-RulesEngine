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
        
        //If we already have tokens of the same type, owner, payload, AND clear trigger, just add to that
        //pile. The payload must match too: distinct RuleGrant payloads (different granted rules) on the
        //same owner are semantically different tokens and must stay separate entries — without this they
        //would collapse into one count, silently dropping every granted rule but the first. The clear
        //trigger must match for the same reason: two same-delta StatModifier grants with different
        //durations ("next roll" vs "this round") are different buffs — merging them would keep only the
        //first arrival's trigger, giving one of them the wrong lifetime. Payload-less same-trigger tokens
        //merge by type+owner exactly as before (Equals(null, null) is true).
        //#197 P12: magnitude tokens are the one exception to payload-equality stacking. Their payload IS
        //their value, so two 0.33 markers must become one 0.67 marker - matching on payload equality
        //would instead leave a separate entry per ignore event, and the merge below would never fire.
        //Type, owner and clear trigger still have to match, for the same reasons as the general case.
        if (token.Payload is TokenPayload.Magnitude incoming)
        {
            int magnitudeIndex = _tokens.FindIndex(t => t.Type == token.Type
                && t.OwnerUnitID == token.OwnerUnitID
                && t.Payload is TokenPayload.Magnitude
                && Equals(t.ClearTrigger, token.ClearTrigger));

            if (magnitudeIndex < 0)
            {
                _tokens.Add(token);
                OnTokenAdded?.Invoke(token);
                return;
            }

            Token existingMagnitude = _tokens[magnitudeIndex];
            float summed = ((TokenPayload.Magnitude)existingMagnitude.Payload!).Value + incoming.Value;
            Token merged = existingMagnitude with { Payload = new TokenPayload.Magnitude(summed) };
            _tokens[magnitudeIndex] = merged;
            OnTokenCountChanged?.Invoke(merged);
            return;
        }

        int existingIndex = _tokens.FindIndex(t => t.Type == token.Type && t.OwnerUnitID == token.OwnerUnitID
            && Equals(t.Payload, token.Payload) && Equals(t.ClearTrigger, token.ClearTrigger));
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

    public int RemoveFirstTriggerTokens(TokenType tokenType, int count = 1)
        => RemoveMatching(entry => entry.Type == tokenType
            && entry.ClearTrigger is TokenClearTrigger.FirstTrigger, count);

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

    public float GetTokenMagnitude(TokenType tokenType)
    {
        float total = 0f;
        foreach (Token token in _tokens.Where(token => token.Type == tokenType))
        {
            if (token.Payload is TokenPayload.Magnitude magnitude)
            {
                total += magnitude.Value * token.Count;
            }
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