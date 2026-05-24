using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public interface IRuleHookBus
{
    public IReadOnlyList<RuleOperation> Dispatch(IHookContext context);
}