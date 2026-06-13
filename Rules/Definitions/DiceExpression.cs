using System.Text.Json.Serialization;

namespace FDG.Rules.Definitions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(D3), "d3")]
[JsonDerivedType(typeof(D6), "d6")]

public abstract record DiceExpression
{
    /// <summary> Number of faces on the die — used to roll the expression. </summary>
    public abstract int Sides { get; }

    public sealed record D3 : DiceExpression
    {
        public override int Sides => 3;
    }

    public sealed record D6 : DiceExpression
    {
        public override int Sides => 6;
    }
}
