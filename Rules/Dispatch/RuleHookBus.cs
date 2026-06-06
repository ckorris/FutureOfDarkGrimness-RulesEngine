using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

public class RuleHookBus : IRuleHookBus
{
    public IReadOnlyList<RuleOperation> Dispatch(IHookContext context)
    {
        return new List<RuleOperation>(); //Temp — real dispatch lands in Phase 7a.
    }

    public IReadOnlyList<AbilityOffer> GatherOffers(IHookContext context)
    {
        return new List<AbilityOffer>(); //Temp — real offer gathering lands in Phase 7c.
    }

    public IReadOnlyList<RuleOperation> ResolveAbility(AbilityOffer offer, IReadOnlyList<IUnit> targets)
    {
        return new List<RuleOperation>(); //Temp — real ability resolution lands in Phase 7c.
    }
}
