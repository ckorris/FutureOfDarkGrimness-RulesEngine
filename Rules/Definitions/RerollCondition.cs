using System.Text.Json.Serialization;

namespace FDG.Rules.Definitions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OnUnmodifiedValue), "onUnmodifiedValue")]
[JsonDerivedType(typeof(AllFailures), "allFailures")]
public abstract record RerollCondition
{
    /// <summary>
    /// Re-roll every unmodified die at or above <see cref="MinValue"/>. Bane / Mischievous / Scrapper
    /// re-roll unmodified 6s and leave it defaulted; their Boost variants widen the band to 5-6 by
    /// authoring <c>minValue: 5</c> (#197).
    ///
    /// <para>The default of 6 is load-bearing for compatibility: every authoring that predates the field
    /// serializes as a bare <c>{"kind":"onUnmodifiedValue"}</c> and must keep meaning "the unmodified
    /// maximum". Composition is by MINIMUM in <c>RerollSink</c>, not by sum - a base and its Boost on the
    /// same weapon net the wider band (5-6) rather than stacking into nonsense, which is why these Boosts
    /// are authored as the FULL band the corpus states rather than as an increment. That differs from the
    /// additive sinks (hit injection, roll modifiers, movement), where a Boost must be the increment.</para>
    /// </summary>
    public sealed record OnUnmodifiedValue(int MinValue = 6) : RerollCondition;

    public sealed record AllFailures : RerollCondition;
}