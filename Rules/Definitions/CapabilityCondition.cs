using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

public abstract record CapabilityCondition<TCap> : Condition where TCap : class, ICapability
{
    protected abstract bool EvaluateCore(TCap context);

    public sealed override bool Evaluate(RuleInvocation invocation)
    {
        return invocation.Hook is TCap typed
            ? EvaluateCore(typed)
            : throw new InvalidOperationException(
                $"{GetType().Name} requires {typeof(TCap).Name}, but the firing context " +
                $"({invocation.Hook.GetType().Name}) does not provide it.");
    }

    public sealed override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(TCap)];
}