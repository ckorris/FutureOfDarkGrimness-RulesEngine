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
            _ => TokenType.HitRollModifier,
        };
    }
}
