namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a hit-count multiplier. A hit-multiplying operation
/// (<see cref="RuleOperation.MultiplyHits"/>, Blast) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the hit-roll stage folds the
/// multiplier without ever inspecting an operation's concrete type. Multiple
/// multipliers compound (product). Distinct from the hit-INJECTION sink (which adds
/// a flat count): Blast multiplies whatever hits already landed, and is applied
/// AFTER injection ("after other rules"), then capped at the target's model count.
/// The read face (the net multiplier) lives on the concrete sink.
/// </summary>
public interface IHitMultiplierSink
{
    void Multiply(int factor);
}
