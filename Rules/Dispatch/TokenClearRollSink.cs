using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Folds token-clear-roll operations (round-start Shaken recovery) into one effective threshold per
/// token type. Multiple sources for the same token don't stack into multiple rolls: the sink keeps
/// the BEST (lowest) threshold — the WoundIgnoreSink discipline — so Vale Oath (4+) plus Vale Oath
/// Boost (3+) is a single roll at 3+ (#376). Distinct rules for the same token fold the same way
/// (owner-ruled facet: Steadfast + Battleborn on one unit would be one roll at 4+, not two chances —
/// no shipped unit carries both). The rolls themselves live with the stage via
/// <see cref="TokenClearRolls"/>: rolling is execution, the sink stays a pure fold.
/// </summary>
public sealed class TokenClearRollSink : ITokenClearRollSink
{
    private readonly List<TokenType> _order = new();
    private readonly Dictionary<TokenType, int> _best = new();

    /// <summary> Write face: records "clear <paramref name="tokenType"/> on
    /// <paramref name="minRoll"/>+", keeping the lowest threshold per token type. </summary>
    public void ClearOn(TokenType tokenType, int minRoll)
    {
        if (!_best.TryGetValue(tokenType, out int existing))
        {
            _order.Add(tokenType);
            _best[tokenType] = minRoll;
        }
        else if (minRoll < existing)
        {
            _best[tokenType] = minRoll;
        }
    }

    /// <summary> Applies every token-clear-roll operation in <paramref name="operations"/>. </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<ITokenClearRollSink> operation
                 in operations.OfType<SinkOperation<ITokenClearRollSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary>
    /// Read face: one (token type, threshold) pair per token type any entry named, in first-seen
    /// order. Thresholds are clamped to [2, 6] — a natural 1 always fails and a natural 6 always
    /// succeeds, so data-authored out-of-range values can never make recovery automatic or impossible.
    /// </summary>
    public IReadOnlyList<(TokenType TokenType, int MinRoll)> Entries =>
        _order.Select(t => (t, DiceUtilities.ClampSuccessRollNeeded(_best[t]))).ToList();
}
