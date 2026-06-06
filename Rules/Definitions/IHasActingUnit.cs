using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// Capability for hooks fired from a single acting unit's perspective (e.g. the unit
/// about to attack at <c>OnPreAttack</c>). Lets the activated-ability path read the
/// offering unit straight off the firing context, so <c>RuleEvaluator.GatherOffers</c>
/// can address it directly without the caller naming it separately.
///
/// Lives in Definitions rather than Foundation because it references <see cref="IUnit"/>
/// (an engine type), same rationale as <see cref="IHasActionType"/>; Foundation stays
/// free of game-object dependencies.
/// </summary>
public interface IHasActingUnit : ICapability
{
    IUnit ActingUnit { get; }
}
