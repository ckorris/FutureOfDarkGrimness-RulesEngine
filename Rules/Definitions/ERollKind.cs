namespace FDG.Rules.Definitions;

/// <summary>
/// Discriminator for which kind of dice roll an <see cref="Effect"/> or
/// <see cref="Condition"/> is targeting. Lets a single
/// <c>RollModifier</c> / <c>Reroll</c> effect serve all three roll types
/// by carrying the kind as data instead of having three sibling subtypes.
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
}
