namespace FDG.Rules.Dispatch;

/// <summary>
/// Capability interface: a hook context that carries a source-to-target distance.
/// Lets <see cref="RuleEvaluator"/> read distance for range-gated conditions
/// (Stealth/Artillery/Relentless &gt; 9") without knowing the concrete context
/// type. Contexts opt in by implementing it; the evaluator never downcasts to a
/// concrete context. More such capability interfaces are added per condition as
/// Phase 7a grows (unmodified rolls, action type, ...).
/// </summary>
public interface IHasDistance
{
    /// <summary> Source-to-target distance in inches for this event. </summary>
    float DistanceInches { get; }
}
