namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a reroll fold (Bane: the defender re-rolls unmodified Defense 6s). An operation
/// that requests a reroll (<see cref="RuleOperation.ApplyReroll"/>) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the stage learns which rerolls to perform without
/// inspecting an operation's concrete type. The read face (which rerolls fired) lives on the concrete
/// sink; the stage owns the actual re-roll of dice.
/// </summary>
public interface IRerollSink
{
    void RequestReroll(ERollKind roll, RerollCondition condition);
}
