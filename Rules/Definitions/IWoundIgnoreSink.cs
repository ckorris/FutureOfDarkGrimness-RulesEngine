namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a wound-ignore fold (Regeneration). An operation that ignores
/// wounds on a roll (<see cref="RuleOperation.IgnoreWound"/>) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the wound stage learns the ignore
/// threshold without ever inspecting an operation's concrete type. The read face (is an
/// ignore active, and at what threshold) lives on the concrete sink — the stage owns the
/// per-wound dice roll, since rolling is execution and the sink stays a pure fold.
/// </summary>
public interface IWoundIgnoreSink
{
    void IgnoreOn(int minRoll);
}
