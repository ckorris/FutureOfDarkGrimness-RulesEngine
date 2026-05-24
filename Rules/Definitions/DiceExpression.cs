namespace FDG.Rules.Definitions;

public abstract record  DiceExpression
{
    public sealed record D3 : DiceExpression;

    public sealed record D6 : DiceExpression;
}