using System.Text.Json.Serialization;

namespace FDG.Rules.Definitions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OnUnmodifiedValue), "onUnmodifiedValue")]
[JsonDerivedType(typeof(AllFailures), "allFailures")]
public abstract record RerollCondition
{
    public sealed record OnUnmodifiedValue : RerollCondition;

    public sealed record AllFailures : RerollCondition;
}