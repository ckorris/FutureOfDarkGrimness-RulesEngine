namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a roll-modifier accumulator. An operation that adjusts a roll
/// (<see cref="RuleOperation.ApplyRollModifier"/>) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so a stage folds roll modifiers without ever
/// inspecting an operation's concrete type. The read face (the net total) lives on the concrete sink.
/// </summary>

public interface IRollModifierSink
{
    void Add(ERollKind roll, int delta);
}