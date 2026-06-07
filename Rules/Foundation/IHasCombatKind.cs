namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hook context that knows whether its attack is melee
/// (vs. shooting). Lets <see cref="Rules.Definitions.Condition.IsMelee"/> gate
/// melee-only rules (Furious) on a hook (hit-roll resolution) that fires in both
/// combat kinds. Shooting is simply the absence of melee — add an explicit
/// IsShooting only if a rule ever needs it.
/// </summary>
public interface IHasCombatKind : ICapability
{
    bool IsMelee { get; }
}
