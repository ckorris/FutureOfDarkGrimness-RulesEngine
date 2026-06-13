using System.Text.Json.Serialization;

namespace FDG.SaveLoad
{
    // PrintableName is derived in every subtype (Name, "Name(N)", "Name (Aliased)"), so it's never
    // persisted — [JsonIgnore] on the settable base property keeps it out of the file (the get-only
    // strip in RuleJson can't reach it). The subtype ctors recompute it on load.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(SpecialRuleEntry_Core), "core")]
    [JsonDerivedType(typeof(SpecialRuleEntry_CoreNumeric), "coreNumeric")]
    [JsonDerivedType(typeof(SpecialRuleEntry_Alias), "alias")]
    public abstract record SpecialRuleEntry([property: JsonIgnore] string PrintableName);

    /// <summary>
    /// Holds a "core" rule, that is, one that appears in the base rules, like "Regeneration".
    /// This does not include ones that have a variable in it.
    /// </summary>
    public record SpecialRuleEntry_Core(string Name) 
        : SpecialRuleEntry(Name);

    /// <summary>
    /// Holds a "core" rule that appears in the base rules and that also has a numeric value
    /// as part of it, like "Tough(X)".
    /// </summary>
    public record SpecialRuleEntry_CoreNumeric(string Name, int NumericValue)
        : SpecialRuleEntry($"{Name}({NumericValue})");


    /// <summary>
    /// Holds a rule that is just a renaming of a different rule and that works the same way.
    /// An example would be Battle Brothers' "Medical Training (Regeneration)".
    /// </summary>
    public record SpecialRuleEntry_Alias(string Name, SpecialRuleEntry AliasedRule)
        :SpecialRuleEntry($"{Name} ({AliasedRule.PrintableName})");
}