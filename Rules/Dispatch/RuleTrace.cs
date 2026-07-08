namespace FDG.Rules.Dispatch;

/// <summary>
/// #163 — process-wide switch for rule-dispatch tracing. When enabled, <see cref="RuleEvaluator"/>
/// narrates every LIVE hook evaluation through the game log's Debug channel
/// (<see cref="ITextOutput.LogDebug"/>): which hook fired with which participants, which rules'
/// entries matched, whether each condition passed or failed (and which condition), what operations
/// were produced, dedup skips, suppression victims, and ability-offer decisions. Read-only query
/// paths (the per-frame <c>EvaluateAllNamed</c> UI queries) never trace.
///
/// This is the manual-testing twin of <see cref="RuleFireLint"/>: the lint proves a rule CAN fire;
/// the trace shows whether it DID fire in the game you are playing — turning "the save numbers
/// look... right?" into "did the expected line appear". Kills the silent-no-op failure mode
/// (SpecialRulesAudit SYS-1/SYS-2 class) during play.
///
/// Static and process-local by design (same shape as <see cref="RuleDiagnostics"/>): evaluations run
/// on the HOST in networked games, so only the host's toggle produces traces; the emitted lines ride
/// the normal debug-log relay to every client, whose front ends hide them unless their own Debug
/// view is on. Enabled by the app via the <c>--trace-rules</c> flag or the GUI console's Debug toggle.
/// </summary>
public static class RuleTrace
{
    public static volatile bool Enabled;
}
