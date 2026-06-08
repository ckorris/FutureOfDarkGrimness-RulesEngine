namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of an impact-dice accumulator. A charge-impact operation
/// (<see cref="RuleOperation.ChargeImpactHits"/>, Impact) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the charge-contact stage folds the
/// total dice to roll without inspecting an operation's concrete type. Multiple
/// Impact(X) sources sum. The read face (the running total) lives on the concrete sink.
/// </summary>
public interface IImpactSink
{
    void AddDice(int count);
}
