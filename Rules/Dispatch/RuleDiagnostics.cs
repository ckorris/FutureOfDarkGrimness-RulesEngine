namespace FDG.Rules.Dispatch;

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
