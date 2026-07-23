using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions
{
    /// <summary>
    /// Maps an <see cref="ERollKind"/> to the <see cref="TokenType"/> that carries a granted numeric
    /// modifier for that roll (#033 stat-modifier primitive). The roll kind lives in the token type rather
    /// than a payload field so different rolls' modifiers never merge in the container, and so Foundation's
    /// <see cref="TokenType"/> needn't reference <see cref="ERollKind"/>. Shared by the granting effect
    /// (<see cref="Effect.StatModifier"/>) and the consuming roll stages.
    /// </summary>
    public static class RollModifierTokens
    {
        public static TokenType TypeFor(ERollKind roll) => roll switch
        {
            ERollKind.Hit => TokenType.HitRollModifier,
            ERollKind.Save => TokenType.SaveRollModifier,
            ERollKind.Morale => TokenType.MoraleRollModifier,
            ERollKind.Cast => TokenType.CastRollModifier,
            // Fail loudly if a new roll kind is added without a carrier token type — a silent fallback
            // here would route the modifier onto hit rolls, a wrong-roll bug that's hard to trace back.
            _ => throw new System.ArgumentOutOfRangeException(nameof(roll), roll,
                "No modifier-carrier TokenType is mapped for this roll kind."),
        };
    }
}
