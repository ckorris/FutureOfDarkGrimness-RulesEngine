namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a wound-count multiplier. A wound-multiplying operation
/// (<see cref="RuleOperation.MultiplyWounds"/>) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the wound stage folds the
/// multiplier without ever inspecting an operation's concrete type. Multiple
/// multipliers compound (product). The read face (the net multiplier) lives on the
/// concrete sink.
/// </summary>
public interface IWoundModifierSink
{
    void Multiply(int factor);
}
