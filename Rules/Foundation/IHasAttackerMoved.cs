namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hook context that reports whether the attacking unit
/// moved before this attack. Lets <see cref="Rules.Definitions.Condition.AfterMoving"/>
/// gate on it (Indirect's -1 to hit when shooting after moving) without the
/// evaluator knowing the concrete context type.
/// </summary>
public interface IHasAttackerMoved : ICapability
{
    bool AttackerMoved { get; }
}
