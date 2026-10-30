namespace FDG.Rules.Foundation;

/// <summary>
/// #435: what made a unit take a Morale Test. OPR 3.6 splits morale rules by it - AoF Banner: "This model and its
/// unit get +1 to Morale Test rolls triggered by shooting/melee" - so a test a spell or a rule demands (Mind Control,
/// Fatigue Debuff, Spell Drain, a spell's "must take a Morale Test") is not one Banner helps with.
/// </summary>
public enum EMoraleTestCause
{
    /// <summary>A test a spell or a special rule demanded, or one whose cause the caller did not say (the default,
    /// so a call site that predates the cause reads as "not shooting, not melee").</summary>
    Other = 0,

    /// <summary>The wound-driven test after shooting left the unit at half strength or less
    /// (<c>ResolveRangedMoraleStage</c>, and the strafe / Crossing Strike attack made "as if it was shooting").</summary>
    Shooting = 1,

    /// <summary>The melee loser's test (<c>RollForMoraleStage</c>).</summary>
    Melee = 2,
}

/// <summary>
/// #435 capability: the hook context knows what triggered the Morale Test being taken. Implemented by
/// <see cref="Dispatch.Contexts.PreMoraleTestContext"/>; read by
/// <see cref="Definitions.Condition.MoraleTestFromShootingOrMelee"/> (OPR 3.6 Banner).
/// </summary>
public interface IHasMoraleTestCause : ICapability
{
    EMoraleTestCause MoraleTestCause { get; }
}
