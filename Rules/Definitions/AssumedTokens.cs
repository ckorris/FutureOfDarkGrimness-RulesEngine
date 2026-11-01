using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// #459: a read-only "assumed tokens" overlay a rule QUERY can carry - for named units, the tokens to treat as
/// present (with a count) or absent, consulted by the token-reading conditions (<see cref="Condition.TokenPresent"/>,
/// <see cref="Condition.CountsAsFatiguedInMelee"/>, <see cref="Condition.TargetShakenOrFatigued"/>) BEFORE the
/// unit's live container. Lets an estimator ask "as if this unit had MovedThisRound" (the Tactician pricing the
/// incoming fire its own Artillery piece would take AFTER the move it is weighing) or "as if Shaken" without
/// touching the shared, synced unit state.
/// <para>
/// Structurally unable to leak into live resolution: the only entry point that accepts one is the read-only
/// <c>RuleEvaluator.EvaluateAllNamed(context, assumed, participants)</c> query (no logging, no one-shot grants
/// spent); <c>Evaluate</c>, <c>EvaluateAll</c> and <c>EvaluateAllNamedLive</c> - the paths the stages drive - have
/// no overload that takes it, so every live invocation carries null and reads the real tokens. The overlay itself
/// is immutable; a unit it names is never written.
/// </para>
/// <para>
/// Scope: the overlay answers the token COUNT questions the conditions above ask. It does not grant rules (a
/// <see cref="TokenType.RuleGrant"/> assumption is not walked for granted rules), and effects that read tokens for
/// their amounts (token-scaled markers) read the live container - add a reader there when an estimate needs it.
/// </para>
/// </summary>
public sealed class AssumedTokens
{
    private readonly (UnitID Unit, TokenType Type, int Count)[] _entries;

    private AssumedTokens((UnitID Unit, TokenType Type, int Count)[] entries) => _entries = entries;

    /// <summary>Assume <paramref name="unit"/> carries <paramref name="count"/> of <paramref name="type"/>.</summary>
    public static AssumedTokens Present(IUnit unit, TokenType type, int count = 1) =>
        new(new[] { (unit.ID, type, Math.Max(0, count)) });

    /// <summary>Assume <paramref name="unit"/> carries no <paramref name="type"/> token, whatever it holds live.</summary>
    public static AssumedTokens Absent(IUnit unit, TokenType type) => Present(unit, type, 0);

    /// <summary>
    /// A copy with one more assumption. A later entry for the same unit and token wins, so a builder can refine
    /// an earlier one.
    /// </summary>
    public AssumedTokens With(IUnit unit, TokenType type, int count)
    {
        var entries = new (UnitID, TokenType, int)[_entries.Length + 1];
        Array.Copy(_entries, entries, _entries.Length);
        entries[_entries.Length] = (unit.ID, type, Math.Max(0, count));
        return new AssumedTokens(entries);
    }

    /// <summary>
    /// The assumed count for <paramref name="unit"/>'s <paramref name="type"/> tokens, when the overlay names that
    /// pair; false means "no assumption - read the live container".
    /// </summary>
    public bool TryGetCount(IUnit unit, TokenType type, out int count)
    {
        // Last match wins (see With). Linear: an overlay holds one or two entries.
        for (int i = _entries.Length - 1; i >= 0; i--)
        {
            if (_entries[i].Unit == unit.ID && _entries[i].Type == type)
            {
                count = _entries[i].Count;
                return true;
            }
        }
        count = 0;
        return false;
    }

    /// <summary>The number of (unit, token) assumptions held - for diagnostics and tests.</summary>
    public int Count => _entries.Length;
}
