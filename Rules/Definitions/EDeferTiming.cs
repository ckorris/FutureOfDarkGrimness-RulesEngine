namespace FDG.Rules.Definitions;

/// <summary>
/// When a unit set aside by <see cref="Effect.DeferDeployment"/> is placed on the table.
/// The deployment subsystem reads this off <see cref="RuleOperation.DeferDeployment"/> to
/// decide which placement pass handles the reserved unit.
///
/// Only <see cref="AfterNormalDeployment"/> (Scout) exists today; Ambush adds a
/// <c>LaterRound</c> value when its slice lands (it arrives from round 2 onward).
/// </summary>
public enum EDeferTiming
{
    /// <summary>
    /// Placed once all normally-deploying units are down, still during the deployment
    /// phase (Scout — within range of the owner's deployment zone).
    /// </summary>
    AfterNormalDeployment = 1,
}
