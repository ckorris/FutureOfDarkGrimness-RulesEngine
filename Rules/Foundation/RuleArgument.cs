namespace FDG.Rules.Foundation;

/// <summary>
/// One argument supplied to a parameterized special rule (Deadly(3), Tough(6),
/// Caster(2)…). A rule carries an ordered, variadic list of these — zero for most
/// rules, one for every parameterized rule in the core book, and more for future
/// army-specific rules. Effects and conditions read an argument by index.
///
/// Closed sum type, same pattern as <see cref="Definitions.Effect"/> and
/// <see cref="Definitions.Condition"/>. Pure data; serializes to a JSON array element.
/// </summary>
public abstract record RuleArgument
{
    /// <summary> An integer argument — the kind every core-book rule uses. </summary>
    public sealed record Int(int Value) : RuleArgument;

    /// <summary>
    /// A text argument — the growth this type's doc anticipated, arrived with #197 P17:
    /// <c>Spawn(Spores [5])</c> / <c>Split(Changelings [10])</c> name the auxiliary unit spec the
    /// bearer places, and that name is data on the rule instance like any other argument.
    /// </summary>
    public sealed record Str(string Value) : RuleArgument;
}
