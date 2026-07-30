namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a defense-set fold (Armor(X): "counts as having Defense X+ in place of the
/// model's own Defense stat"). An operation that sets defense (<see cref="RuleOperation.SetDefense"/>)
/// applies itself here via <see cref="SinkOperation{TSink}.ApplyTo"/>, so the creation applicator
/// learns the set value without inspecting an operation's concrete type. A literal SET of the BASE
/// stat, not a floor — per-roll modifiers still apply on top. The read face lives on the concrete sink.
/// </summary>
public interface IDefenseSetSink
{
    void SetTo(int defense);
}
