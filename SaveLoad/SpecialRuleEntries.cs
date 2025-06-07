
namespace FDG.SaveLoad
{
    public abstract record SpecialRuleEntry(string PrintableName);

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