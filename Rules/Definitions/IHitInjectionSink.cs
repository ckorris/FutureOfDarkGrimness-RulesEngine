namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of an extra-hit accumulator. An operation that injects hits
/// (<see cref="RuleOperation.InsertExtraHits"/>) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the hit-roll stage folds extra
/// hits without ever inspecting an operation's concrete type. Unlike the roll and
/// movement sinks (whose total a stage reads back as a modifier), the total here
/// becomes new synthetic hits appended to the successful-hit list. The read face
/// (the running total) lives on the concrete sink.
/// </summary>
public interface IHitInjectionSink
{
    void AddHits(float count);
}
