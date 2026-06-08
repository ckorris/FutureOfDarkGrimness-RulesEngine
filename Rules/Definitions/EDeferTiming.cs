namespace FDG.Rules.Definitions;

/// <summary>
/// When a unit set aside by <see cref="Effect.DeferDeployment"/> is placed on the table.
/// The deployment subsystem reads this off <see cref="RuleOperation.DeferDeployment"/> to
/// decide which placement pass handles the reserved unit.
///
/// <see cref="AfterNormalDeployment"/> is Scout; <see cref="LaterRound"/> is Ambush.
/// </summary>
public enum EDeferTiming
{
    /// <summary>
    /// Placed once all normally-deploying units are down, still during the deployment
    /// phase (Scout — within range of the owner's deployment zone).
    /// </summary>
    AfterNormalDeployment = 1,

    /// <summary>
    /// Kept in reserve and brought on at the start of a round after the first, at the
    /// owner's choice (Ambush — placed anywhere over the range distance from enemies).
    /// </summary>
    LaterRound = 2,
}
