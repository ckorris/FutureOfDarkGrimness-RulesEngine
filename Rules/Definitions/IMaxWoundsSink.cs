namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a max-wounds fold (Tough: "this model has X wounds"). An operation that sets
/// max wounds (<see cref="RuleOperation.SetMaxWounds"/>) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the creation applicator learns the wound count
/// without inspecting an operation's concrete type. The read face lives on the concrete sink.
/// </summary>
public interface IMaxWoundsSink
{
    void SetMax(int wounds);
}
