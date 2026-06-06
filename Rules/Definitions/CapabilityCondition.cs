using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

public abstract record CapabilityCondition<TCap> : Condition where TCap : class, ICapability
{
    protected abstract bool EvaluateCore(TCap context);

    public sealed override bool Evaluate(IHookContext context)
    {
        return context is TCap typed
            ? EvaluateCore(typed)
            : throw new InvalidOperationException(
                $"{GetType().Name} required {typeof(TCap).Name} but the firing context, {context.GetType().Name} doesn't provide it.");
    }

    public sealed override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(TCap)];
}