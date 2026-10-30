namespace FDG.Rules.Foundation;

/// <summary>
/// #432 capability: how far away the attack was, split the way OPR 3.6's range-conditional army rules read it -
/// "when this model shoots/charges enemies over 9"/6" away", "when shot/charged from over 9"/6" away". The two
/// numbers are separate (each is null when that kind of attack is not what happened) because the rules give each
/// its own threshold, and because a melee swing's live distance is base contact, never the distance the charge was
/// launched from.
/// <para>
/// Implemented by the attack contexts (hit-roll modifier, hit-roll complete, save-roll complete), where it describes
/// the attack in flight, and by <see cref="Dispatch.Contexts.PreMoraleTestContext"/>, where it describes the attack
/// that TRIGGERED the test (a shooting attack, or the charge that the losing unit received).
/// </para>
/// <para>
/// Read by <see cref="Definitions.Condition.AttackingFromOver"/> (Actor seat) and
/// <see cref="Definitions.Condition.AttackedFromOver"/> (Subject seat / the tested unit). Supersedes the conflated
/// single number of <see cref="IHasAttackOriginDistance"/> for 3.6 data; that capability stays for the 3.5
/// <c>attackedFromOverInches</c> kind that old army files and the not-yet-rebaked books embed.
/// </para>
/// </summary>
public interface IHasShootChargeDistance : ICapability
{
    /// <summary>
    /// The shooting distance when this is (or was triggered by) a shooting attack: closest living models, base to
    /// base, attacker to target - the measurement every shooting range gate reads (#431's
    /// <c>targetOverHalfWeaponRange</c> included). Null in melee, for spell damage (a spell is not shooting), and
    /// for a morale test no shooting attack triggered.
    /// </summary>
    float? ShootingDistanceInches { get; }

    /// <summary>
    /// The charge distance when this is (or was triggered by) a charge: the pre-charge gap the charger closed,
    /// closest living models base to base, snapshotted at the charger's activation start, before its charge move
    /// (<c>UnitActionContext.SnapshotDistancesToEnemies</c> -> <c>CombatActionContext</c> ->
    /// <c>CombatMetadata.ChargeOriginDistanceInches</c>). Null when shooting, for a melee swing that is not the
    /// charger's (a strike-back), and for a morale test no charge triggered.
    /// </summary>
    float? ChargeDistanceInches { get; }
}
