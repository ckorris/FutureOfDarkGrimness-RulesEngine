namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hook context that can report how far away the target was when this attack
/// was <i>launched</i>, as opposed to how far away it is now (<see cref="IHasDistance"/>).
///
/// The two differ only in melee. A shooting attack is launched from where the shooter stands, so the
/// launch distance and the live distance are the same. A melee attack is resolved in base contact, so
/// the live distance is always under <c>MELEE_RANGE_INCHES_HORIZONTAL</c> (2") and a
/// <see cref="Definitions.Condition.DistanceGreaterThan"/>(9) gate can never pass there — which silently
/// disabled the melee half of every "when it shoots <b>or charges</b> enemies over 9\" away" rule in the
/// corpus (#197). The launch distance for a charge is the distance to the defender at the instant the
/// charging unit's activation began, before it moved: this engine models Charge as the melee attack and
/// the preceding Move as a separate action, so activation start is where the charge was declared from.
///
/// Read by <see cref="Definitions.Condition.AttackedFromOverInches"/>, which is the whole reason this
/// exists — the six Boost rules that share the "shoots or charges over 9\" away" wording each need one
/// condition, not a hand-composed Or() of a shooting arm and a charging arm.
/// </summary>
public interface IHasAttackOriginDistance : ICapability
{
    /// <summary>
    /// Distance in inches between attacker and target at the moment this attack was launched: the live
    /// distance for a shooting attack, the activation-start distance to the defender for a charge, and
    /// 0 for a melee swing that is not a charge (a strike-back was never launched from anywhere).
    /// </summary>
    float AttackOriginDistanceInches { get; }
}
