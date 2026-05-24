namespace FDG.Rules.Foundation;

/// <summary>
/// Allegiance filter used by <see cref="TargetSelector"/> to restrict the candidate set
/// of an activated ability or spell relative to the source unit.
/// </summary>
public enum ETargetAffinity
{
    /// <summary>
    /// Only the source unit itself is a valid target. Used by self-buff abilities like
    /// <i>Versatile Attack</i> and <i>Versatile Reach</i>.
    /// </summary>
    Self = 0,

    /// <summary>
    /// Only units controlled by the same player as the source. Used by healing, buffing,
    /// and friendly-target spells like <i>Mend</i>, <i>Blessed Ammo</i>, and
    /// <i>Protective Dome</i>.
    /// </summary>
    Friend = 1,

    /// <summary>
    /// Only units controlled by an opposing player. Used by offensive abilities like
    /// <i>Cerebral Trauma</i>, <i>Lightning Fog</i>, and <i>Unstoppable Mark</i>.
    /// </summary>
    Foe = 2,

    /// <summary>
    /// No allegiance filter — any unit on the table is a valid candidate, subject to
    /// the other filters in the selector. Used by allegiance-neutral abilities such as
    /// terrain interactions or future objective-marker effects.
    /// </summary>
    Any = 3,
}
