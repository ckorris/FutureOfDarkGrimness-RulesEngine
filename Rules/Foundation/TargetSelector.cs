namespace FDG.Rules.Foundation;

/// <summary>
/// Describes which units (or models) an activated ability or spell may pick as its
/// target(s). Stored on an <c>ActivatedAbility</c>; consulted by the engine when it
/// raises a target-selection request to the active player.
///
/// The engine uses a selector to build the candidate set:
/// 1. Start from all entities matching <see cref="TargetAffinity"/>.
/// 2. Filter to those within <see cref="RangeInches"/> of the source.
/// 3. If <see cref="RequireLineOfSight"/>, drop occluded candidates.
/// 4. If <see cref="RequiredToken"/> is non-null, drop candidates that don't carry that token.
/// 5. Require the player to pick between <see cref="MinCount"/> and <see cref="MaxCount"/>
///    of the survivors.
/// </summary>
/// <param name="RangeInches">
/// Maximum distance from the source unit at which a candidate may be picked. Measured
/// closest-model to closest-model per the standard rule. Float because some rules use
/// fractional ranges, even though most are whole inches (3", 6", 9", 12", 18").
/// </param>
/// <param name="MinCount">
/// Minimum number of targets that must be selected for the ability to fire. Typically 1.
/// A selector with <see cref="MinCount"/> = 0 means the player may decline (rare).
/// </param>
/// <param name="MaxCount">
/// Maximum number of targets that may be selected. Spells like <i>Blessed Ammo</i> pick
/// "up to two friendlies"; that's <see cref="MinCount"/> = 1, <see cref="MaxCount"/> = 2.
/// Single-target abilities use <see cref="MinCount"/> = <see cref="MaxCount"/> = 1.
/// </param>
/// <param name="TargetAffinity">
/// Restricts the candidate set by allegiance to the source unit
/// (<see cref="ETargetAffinity.Friend"/>, <see cref="ETargetAffinity.Foe"/>,
/// <see cref="ETargetAffinity.Self"/>, or <see cref="ETargetAffinity.Any"/>).
/// </param>
/// <param name="RequireLineOfSight">
/// If true, candidates must be in line of sight of the source unit (per the engine's
/// <c>LineOfSightUtilities</c>). Most "pick a unit in X inches" abilities require LoS;
/// area-of-effect and aura-style targeting often don't.
/// </param>
/// <param name="RequiredToken">
/// Optional. If non-null, candidates must already carry at least one token of this type.
/// Used by abilities that only work against tagged targets (e.g. only re-targets units
/// previously marked by <i>Unstoppable Mark</i>). Null = no token filter (the common case).
/// </param>
/// <param name="RequiredRule">
/// Optional. If non-null, candidates must carry a rule of this name (matched case-insensitively
/// against the attached rule's canonical or requested name, like <c>Condition.UnitHasRule</c>).
/// Used by abilities that only target units with a specific rule (e.g. Re-Position Artillery's
/// "pick one friendly model within 6&quot; with Artillery"). Null = no rule filter (the common case).
/// </param>
/// <param name="SingleModel">
/// #034 single-model targeting. When true, a damage spell resolves against ONE chosen model in the
/// picked unit ("as if the target was a unit of [1]" — Total Seizure, Psy-Destruction): after the unit
/// is selected the caster picks a model, and all wounds funnel to it with no carry-over to the rest of
/// the unit (the same confinement Takedown uses, via <c>IndividualTargetResult</c>). Pair with
/// <see cref="MaxCount"/> = 1. Default false → the normal whole-unit allocation. Only meaningful for
/// damage (<c>Effect.DealHits</c>) spells; ignored by buff/debuff effects.
/// </param>
public record TargetSelector(float RangeInches, int MinCount, int MaxCount, ETargetAffinity TargetAffinity,
    bool RequireLineOfSight, TokenType? RequiredToken = null, bool SingleModel = false,
    string? RequiredRule = null);
