namespace FDG.Rules.Tokens;

/// <summary>
/// #408: the tokens a unit and its models carried at one moment, so a backed-out activation can put them
/// back. Activation-start abilities that only grant rules (Versatile Attack/Defense/Reach, Watchborn, the
/// Feats) change a unit through tokens alone - the granted rule and the spent once-per-activation or
/// once-per-game marker - so restoring the tokens undoes them completely, with no per-ability undo code.
/// Anything that changes more than tokens (dice, placements, spawned units) must mark the activation
/// irreversible instead, which takes back-out off the table.
/// </summary>
public sealed class TokenSnapshot
{
    private readonly List<(ITokenContainer Container, IReadOnlyList<Token> Tokens)> _entries;

    private TokenSnapshot(List<(ITokenContainer, IReadOnlyList<Token>)> entries) => _entries = entries;

    public static TokenSnapshot Capture(IUnit unit)
    {
        var entries = new List<(ITokenContainer, IReadOnlyList<Token>)>
        {
            (unit.Tokens, unit.Tokens.GetAllTokens().ToList()),
        };
        foreach (IModel model in unit.Models)
        {
            entries.Add((model.Tokens, model.Tokens.GetAllTokens().ToList()));
        }
        return new TokenSnapshot(entries);
    }

    public void Restore()
    {
        foreach ((ITokenContainer container, IReadOnlyList<Token> tokens) in _entries)
        {
            container.RestoreTokens(tokens);
        }
    }
}
