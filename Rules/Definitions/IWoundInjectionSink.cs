namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of an extra-wound accumulator. <see cref="RuleOperation.InsertExtraWounds"/>
/// applies itself here via <see cref="SinkOperation{TSink}.ApplyTo"/>, so the wound-assignment
/// stage folds extra wounds (Shred) without ever inspecting an operation's concrete type. The
/// wound-side mirror of <see cref="IHitInjectionSink"/>; the running total lives on the concrete sink.
/// </summary>
public interface IWoundInjectionSink
{
    void AddWounds(float count);
}
