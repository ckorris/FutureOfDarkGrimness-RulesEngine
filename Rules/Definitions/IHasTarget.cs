using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Capability for hooks where an attacker's rule resolves against a specific defending unit —
/// the hit / save / wound contexts. Lets target-keyed conditions read that unit without the
/// evaluator knowing the concrete context type: <see cref="Condition.TargetHasRule"/> ("vs units
/// with rule X"), <see cref="Condition.TargetMajorityHasTough"/> and
/// <see cref="Condition.StatGreaterOrEqualTo"/> ("vs Tough(3)+", "vs Defense X").
///
/// Lives in Definitions rather than Foundation because it references <see cref="IUnit"/> (an
/// engine type), same rationale as <see cref="IHasActingUnit"/>; Foundation stays free of
/// game-object dependencies.
/// </summary>
public interface IHasTarget : ICapability
{
    /// <summary> The unit the firing rule resolves against (the defender / chosen target). </summary>
    IUnit Target { get; }
}
