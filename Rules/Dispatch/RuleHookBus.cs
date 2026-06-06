using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

public class RuleHookBus : IRuleHookBus
{
    // Vestigial stub: passive dispatch and activated abilities both moved to
    // RuleEvaluator. Dispatch still backs harness.Fire (the no-rules smoke test and the
    // 7g owner-destroyed cleanup test) and returns no operations until that lifecycle
    // work (TokenClearService on OnUnitDestroyed) lands.
    public IReadOnlyList<RuleOperation> Dispatch(IHookContext context)
    {
        return new List<RuleOperation>();
    }
}
