namespace FDG.Rules.Definitions;

public abstract record RerollCondition
{
    public sealed record OnUnmodifiedValue : RerollCondition;

    public sealed record AllFailures : RerollCondition;
}