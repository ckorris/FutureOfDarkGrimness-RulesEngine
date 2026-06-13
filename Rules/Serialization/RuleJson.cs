using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FDG.Rules.Serialization;

public static class RuleJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        {
            Modifiers = { DropComputedRuleProperties },
        },
    };

    // Rule definitions are records, so their persisted state is exactly their constructor parameters —
    // which surface as init-settable properties. Any get-only property is a computed/derived convenience
    // for the engine, not state: e.g. Condition/Effect.RequiredCapabilities (which STJ can't even
    // serialize — it's IReadOnlyCollection<Type>) and DiceExpression.Sides (redundant with the "kind" tag).
    // Dropping every setter-less property removes them all without naming any specific type.
    //
    // WEAKNESS: this leans on "get-only == derived", which holds only because every rule type is a record.
    // A hand-written immutable *class* whose get-only property is bound to a constructor parameter would be
    // dropped wrongly — STJ reads such a property via the ctor, so .Set is null even though it IS real state.
    // None exist today; if one were added, ModifierKeepsEveryConstructorParameter (the Step 5 guard) fails.
    private static void DropComputedRuleProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }
        
        for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            if (typeInfo.Properties[i].Set is null)
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }
}