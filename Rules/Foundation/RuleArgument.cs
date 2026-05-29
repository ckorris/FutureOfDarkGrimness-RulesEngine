namespace FDG.Rules.Foundation;

/// <summary>
/// One argument supplied to a parameterized special rule (Deadly(3), Tough(6),
/// Caster(2)…). A rule carries an ordered, variadic list of these — zero for most
/// rules, one for every parameterized rule in the core book, and more for future
/// army-specific rules. Effects and conditions read an argument by index.
///
/// Closed sum type, same pattern as <see cref="Definitions.Effect"/> and
/// <see cref="Definitions.Condition"/>. Only <see cref="Int"/> is needed today
/// (every core-book argument is an integer); other value kinds — e.g. a
/// <c>Str</c> case for Alien Hives' Spawn(unit-type) — are one-line additions
/// when a rule first needs them. Pure data; serializes to a JSON array element.
/// </summary>
public abstract record RuleArgument
{
    /// <summary> An integer argument — the only kind any core-book rule uses. </summary>
    public sealed record Int(int Value) : RuleArgument;
}
