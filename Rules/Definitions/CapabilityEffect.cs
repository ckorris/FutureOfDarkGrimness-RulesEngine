using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

public abstract record CapabilityEffect<TCap> : Effect where TCap : class, ICapability
{
    protected abstract void ApplyCore(TCap context, List<RuleOperation> operations);

    public sealed override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
    {
        if (ruleInvocation.Hook is TCap typed)
        {
            ApplyCore(typed, operations);
            return;
        }

        throw new InvalidOperationException(
            $"{GetType().Name} requires {typeof(TCap).Name}, but the firing context " +
            $"({ruleInvocation.Hook.GetType().Name}) does not provide it.");
    }

    public sealed override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(TCap)];
}