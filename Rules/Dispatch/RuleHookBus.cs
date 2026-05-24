using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public class RuleHookBus : IRuleHookBus
{
    public IReadOnlyList<RuleOperation> Dispatch(IHookContext context)
    {
        return new  List<RuleOperation>(); //Temp.
    }
}