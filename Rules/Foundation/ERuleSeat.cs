namespace FDG.Rules.Foundation;

/// <summary>
/// Which side of an event a rule fires from — the perspective half of a "when"
/// (the <see cref="EHookID"/> being the moment half). A rule fires only when the
/// unit carrying it is playing the matching role in the firing context, so a
/// defensive rule never triggers while its owner is attacking.
///
/// Generalizes the old <c>ISpecialRule_Attacker</c> / <c>ISpecialRule_Defender</c>
/// split into data, and stretches past combat: a moving unit is simply the
/// <see cref="Actor"/> with no <see cref="Subject"/>.
/// </summary>
public enum ERuleSeat
{
    /// <summary>
    /// The unit performing the action this hook fires for — the shooter, mover,
    /// or charger. Furious (the attacker adds hits to its own attack) sits here.
    /// Default seat: most rules act from the side that is "doing" something.
    /// </summary>
    Actor = 0,

    /// <summary>
    /// The unit the action is done to — the one being shot, charged, or saved
    /// against. Stealth (the unit being shot lowers the incoming roll) sits here.
    /// </summary>
    Subject = 1,
}
