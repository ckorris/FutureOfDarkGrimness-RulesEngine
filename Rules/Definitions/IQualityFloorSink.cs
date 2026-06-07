namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a quality-floor fold (Reliable: "this unit's Quality is treated as 2+").
/// An operation that floors quality (<see cref="RuleOperation.QualityFloor"/>) applies itself here
/// via <see cref="SinkOperation{TSink}.ApplyTo"/>, so the hit stage learns the best floor without
/// inspecting an operation's concrete type. The floor sets the BASE quality before per-roll
/// modifiers apply (the rulebook's "still modifiable"). The read face lives on the concrete sink.
/// </summary>
public interface IQualityFloorSink
{
    void FloorTo(int quality);
}
