namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hook context that knows whether the attacking unit is
/// charging (i.e. resolving the melee it initiated this activation, vs. striking
/// back). Lets <see cref="Rules.Definitions.Condition.IsCharging"/> gate
/// charge-only rules (Thrust, and the deferred Furious charge gate) on hit/save
/// hooks that fire in both the charger's swing and the defender's strike-back.
/// Melee is only ever entered via a Charge action, so the charger's swing is
/// charging and the strike-back is not.
/// </summary>
public interface IHasCharging : ICapability
{
    bool IsCharging { get; }
}
