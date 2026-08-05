namespace FDG.Rules.Dispatch;

/// <summary>Why an army-list rule reference was dropped at load time (#168).</summary>
public enum ERuleDropReason
{
    /// <summary>The name has no definition in the registry, and none in the current rulebook either — valid but not yet implemented.</summary>
    Unimplemented,
    /// <summary>
    /// The name has no definition in the registry, but the current rulebook DOES define it (#342): the
    /// saved list predates the rule's implementation and froze a copy of its book's definitions without
    /// it. Distinct from <see cref="Unimplemented"/> because the fix is to rebuild the list, not to wait
    /// for the rule to be built. Only reachable when the list's faction matches no bundled book — when
    /// one matches, load gap-fills the definition and the reference resolves normally.
    /// </summary>
    OutdatedList,
    /// <summary>The rule resolved but its declared scope doesn't fit where the list attached it.</summary>
    WrongScope,
    /// <summary>The rule's effects read an argument the list entry doesn't supply (e.g. a missing numeric value).</summary>
    MissingArgument,
    /// <summary>A weapon rule granted at unit level, on a unit that carries no weapons to re-home it onto.</summary>
    NoWeaponsToAttach,
}

/// <summary>
/// One dropped rule reference, structured for aggregation (#168): the UI turns a load's drops into
/// "N rules on this list are not implemented: ..." without parsing warning strings.
/// </summary>
public readonly record struct RuleDrop(string RuleName, string Owner, ERuleDropReason Reason, string Message);

/// <summary>
/// Central warn channel for rule-load and rule-dispatch diagnostics: unimplemented rule names,
/// wrong-scope attachments, arity mismatches, unresolvable granted rules. These were previously
/// <see cref="System.Diagnostics.Debug"/>-only — compiled out of Release builds entirely, so a player
/// fielding an army whose rules silently did nothing got no signal at all.
///
/// A host (FDGServer / the app) subscribes <see cref="OnWarning"/> to surface warnings in the game log
/// or lobby UI; with no subscriber, warnings fall back to stdout so they are never invisible again.
/// </summary>
public static class RuleDiagnostics
{
    /// <summary> Raised for every rule diagnostic. Subscribe to surface warnings in the UI/game log. </summary>
    public static event Action<string>? OnWarning;

    /// <summary>
    /// Raised for every dropped army-list rule reference, before the same drop's <see cref="OnWarning"/>
    /// string. Subscribers that aggregate (the #168 launch summary) listen here; log-line subscribers
    /// keep using <see cref="OnWarning"/>, which fires for these too.
    /// </summary>
    public static event Action<RuleDrop>? OnRuleDropped;

    /// <summary>Reports a dropped rule reference on both channels (structured, then string).</summary>
    public static void WarnDropped(RuleDrop drop)
    {
        OnRuleDropped?.Invoke(drop);
        Warn(drop.Message);
    }

    public static void Warn(string message)
    {
        Action<string>? handler = OnWarning;
        if (handler != null)
        {
            handler(message);
        }
        else
        {
            Console.WriteLine($"[rules] {message}");
        }
    }

    private static readonly HashSet<string> _warnedKeys = new();

    /// <summary>
    /// Warns at most once per <paramref name="key"/> for the process lifetime. For dispatch-time
    /// diagnostics that would otherwise repeat on every hook evaluation (e.g. an unresolvable granted
    /// rule re-detected every roll). Load-time diagnostics should use <see cref="Warn"/> directly.
    /// </summary>
    public static void WarnOnce(string key, string message)
    {
        lock (_warnedKeys)
        {
            if (!_warnedKeys.Add(key))
            {
                return;
            }
        }

        Warn(message);
    }
}
