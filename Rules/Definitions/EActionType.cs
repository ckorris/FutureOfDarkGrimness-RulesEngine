namespace FDG.Rules.Definitions;

/// <summary>
/// The four activation actions a unit may declare per the GDF rulebook.
/// Used by <see cref="Condition.ActionTypeIs"/> to gate effects on the
/// specific action the bearer just chose (e.g. Rapid Rush applies only
/// when the action is <see cref="Rush"/>).
///
/// Note: Shooting is not an action — it's a sub-step that follows Hold and
/// Advance. Spells are activated abilities triggered inside an existing
/// action, not their own action type. When custom actions (WorkItem #010)
/// land, this enum gains an additional value.
/// </summary>
public enum EActionType
{
    /// <summary>
    /// Unit does not move; may shoot.
    /// </summary>
    Hold = 1,

    /// <summary>
    /// Unit moves up to its standard move distance; may shoot after moving.
    /// </summary>
    Advance = 2,

    /// <summary>
    /// Unit moves up to its rush distance (typically double advance);
    /// may not shoot.
    /// </summary>
    Rush = 3,

    /// <summary>
    /// Unit moves up to its charge distance toward an enemy in an attempt
    /// to reach base contact and initiate melee.
    /// </summary>
    Charge = 4,
}
