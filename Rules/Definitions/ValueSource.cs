namespace FDG.Rules.Definitions;

/// <summary>
/// Where an effect's numeric value comes from: either a fixed literal authored on
/// the effect, or one of the bearing rule's arguments by index. Lets a single
/// arg-driven effect (Deadly's multiplier, Tough's wound count, Caster's token
/// count) be authored once and resolved per-instance against the firing rule's
/// <see cref="Dispatch.ResolvedRule.Arguments"/>.
///
/// Only new arg-driven effects use <see cref="ValueSource"/>; existing fixed-value
/// effects (RollModifier, AddExtraHit, MovementBonus) keep their plain ints. The
/// dispatcher resolves <see cref="Arg"/> against the rule instance whose effect is
/// currently firing.
/// </summary>
public abstract record ValueSource
{
    /// <summary> A fixed value authored directly on the effect. </summary>
    public sealed record Literal(int Value) : ValueSource;

    /// <summary> The bearing rule's argument at <see cref="Index"/> (e.g. Deadly's X is <c>Arg(0)</c>). </summary>
    public sealed record Arg(int Index) : ValueSource;
}
