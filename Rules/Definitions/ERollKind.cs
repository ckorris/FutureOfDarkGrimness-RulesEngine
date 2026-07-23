namespace FDG.Rules.Definitions;

/// <summary>
/// Discriminator for which kind of dice roll an <see cref="Effect"/> or
/// <see cref="Condition"/> is targeting. Lets a single
/// <c>RollModifier</c> / <c>Reroll</c> effect serve all the roll types
/// by carrying the kind as data instead of having sibling subtypes.
/// </summary>
public enum ERollKind
{
    /// <summary>
    /// The hit roll — Quality test made to determine if an attack lands.
    /// </summary>
    Hit = 1,

    /// <summary>
    /// The save (defense) roll made by the defender against each landed hit.
    /// </summary>
    Save = 2,

    /// <summary>
    /// The morale test — Quality test made when a unit takes wounds reducing
    /// it to half size or loses a melee.
    /// </summary>
    Morale = 3,

    /// <summary>
    /// The cast roll — the single die a Caster rolls against the 4+ cast threshold
    /// (#033/#034). Unlike the other three this is not a Quality test: it is a flat
    /// threshold shifted by boost, assists, and granted modifiers of this kind.
    /// </summary>
    Cast = 4,
}
