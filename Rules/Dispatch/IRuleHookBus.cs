using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

public interface IRuleHookBus
{
    /// <summary>
    /// Fires a hook for passive rules: returns the resolved operation queue produced
    /// by every matching <see cref="HookEntry"/> on the relevant units.
    /// </summary>
    public IReadOnlyList<RuleOperation> Dispatch(IHookContext context);
}
