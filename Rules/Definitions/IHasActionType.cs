using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Capability interface: a hook context that carries the action type the bearer
/// just declared. Lets <see cref="Condition.ActionTypeIs"/> gate on the declared
/// action (Fast/Slow on Advance, Immobile's not-Hold check) without the evaluator
/// knowing the concrete context type.
///
/// Lives in Definitions (not Foundation, where the other capabilities sit) because
/// it references <see cref="EActionType"/>, which is a Definitions type — Foundation
/// must stay dependency-free.
/// </summary>
public interface IHasActionType : ICapability
{
    EActionType ActionType { get; }
}
