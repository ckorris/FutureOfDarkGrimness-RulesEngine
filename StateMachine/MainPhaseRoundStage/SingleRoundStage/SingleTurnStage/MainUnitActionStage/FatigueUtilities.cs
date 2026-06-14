using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// #020 Fatigue: a unit that charges or strikes back in melee becomes Fatigued for the rest of the
    /// round, after which it hits only on unmodified 6s in further melee. Shaken units (#089) always
    /// count as fatigued in melee. The token clears at end of round via the <c>Round_OnRoundEnd</c>
    /// hook (<see cref="TokenClearTrigger.RoundEnd"/>), swept by <c>ReconcileObjectivesStage</c>.
    /// </summary>
    public static class FatigueUtilities
    {
        /// <summary>Mark a unit Fatigued for the rest of the round; applied idempotently.</summary>
        public static void ApplyFatigued(IUnit unit)
        {
            if (unit.Tokens.HasToken(TokenType.Fatigued)) return;

            unit.Tokens.AddToken(new Token(TokenType.Fatigued, 1, new TokenClearTrigger.RoundEnd()));
        }

        /// <summary>
        /// Whether a unit hits only on unmodified 6s in melee: it is Fatigued, or it is Shaken (#089),
        /// which always counts as fatigued in melee.
        /// </summary>
        public static bool CountsAsFatiguedInMelee(IUnit unit)
            => unit.Tokens.HasToken(TokenType.Fatigued) || unit.Tokens.HasToken(TokenType.Shaken);
    }
}
