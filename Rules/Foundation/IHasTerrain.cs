using System.Collections.Generic;

namespace FDG.Rules.Foundation;

/// <summary>
/// Capability: the table's terrain pieces, exposed at a hook so a rule can reason about the firing
/// unit's position relative to terrain - the Grounded family ("if a unit ... has most of them within 1in
/// of terrain, ..."). Terrain is table-level and seat-independent; the CONDITION applies it to the firing
/// rule's <see cref="RuleInvocation.Bearer"/>, which is why this carries the whole list rather than a
/// precomputed per-unit flag (the same context serves both the attacker and the defender seat).
///
/// Contexts that cannot supply terrain - AI combat valuation, synthetic Blast/Strafing hits - carry an
/// empty list, so a terrain-gated rule simply does not fire on those paths. That is a conservative
/// default (the bonus is only ever omitted, never wrongly granted), documented at each such call site.
/// </summary>
public interface IHasTerrain : ICapability
{
    /// <summary> The table's terrain pieces; empty when the firing context cannot supply them. </summary>
    IReadOnlyList<ITerrain> Terrain { get; }
}
