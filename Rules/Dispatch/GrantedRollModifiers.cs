using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Reads — and, for one-shot "next time" grants, consumes — a unit's granted numeric roll modifiers
    /// (#033 <see cref="Effect.StatModifier"/>). The relevant roll stage calls <see cref="ConsumeNet"/> for
    /// the rolling unit and roll kind, then folds the returned net delta into its own roll-modifier math
    /// (same sign convention as the rule-driven roll-modifier sink: a positive delta makes the roll easier).
    /// FirstTrigger ("once") grants are removed here — this roll is that "next time"; duration grants
    /// (ThisActivation / ThisRound) are left for <see cref="Tokens.TokenClearService"/> to sweep at their hook.
    /// </summary>
    public static class GrantedRollModifiers
    {
        /// <summary>
        /// Read-only twin of <see cref="ConsumeNet"/> (#325 pre-roll forecast): the same net delta the
        /// next roll will fold in, computed WITHOUT removing the one-shot grants that roll will spend.
        /// Safe to call while building UI; the forecast stays honest because each offer pass recomputes
        /// it, so a grant spent by an earlier volley of the same action has already left the tokens.
        /// </summary>
        public static int PeekNet(IUnit unit, ERollKind roll, bool ignorePositive = false,
            bool ignoreOtherUnitsNegative = false)
        {
            TokenType type = RollModifierTokens.TypeFor(roll);

            int net = 0;
            foreach (Token token in unit.Tokens.GetAllTokens(type))
            {
                if (token.Payload is TokenPayload.StatModifier modifier
                    && !(ignorePositive && modifier.Delta > 0)
                    && !IsIgnoredNegative(token, modifier, ignoreOtherUnitsNegative))
                {
                    net += modifier.Delta * token.Count;
                }
            }
            return net;
        }

        /// <summary>
        /// #427 Unstoppable: a negative grant another unit placed (<see cref="Token.OwnerUnitID"/> set - an enemy
        /// spell's "-1 to hit") is "not from this model's special rules", so an attack that ignores negative
        /// weapon modifiers leaves it out. A negative grant the bearer placed on itself (OwnerUnitID null) is its
        /// own and still counts.
        /// </summary>
        private static bool IsIgnoredNegative(Token token, TokenPayload.StatModifier modifier, bool ignoreOtherUnitsNegative) =>
            ignoreOtherUnitsNegative && modifier.Delta < 0 && token.OwnerUnitID != null;

        /// <param name="ignorePositive">#431 Pierce ("ignores all positive Defense modifiers on the target"): leave
        /// the positive grants out of the net. They are still spent like any one-shot grant - this roll IS the
        /// "next time" - so an ignored "+1 to Defense once" does not carry over to the next attack (#431
        /// decision).</param>
        /// <param name="ignoreOtherUnitsNegative">#427 Unstoppable: leave the negative grants another unit placed out
        /// of the net. Spent like any one-shot grant all the same - the attack IS the "next time" - so an ignored
        /// "-1 to hit once" does not carry over to the next attack (the same call as Pierce's #431 decision).</param>
        public static int ConsumeNet(IUnit unit, ERollKind roll, bool ignorePositive = false,
            bool ignoreOtherUnitsNegative = false)
        {
            TokenType type = RollModifierTokens.TypeFor(roll);

            int net = 0;
            int firstTriggerCount = 0;
            foreach (Token token in unit.Tokens.GetAllTokens(type).ToList())
            {
                if (token.Payload is TokenPayload.StatModifier modifier
                    && !(ignorePositive && modifier.Delta > 0)
                    && !IsIgnoredNegative(token, modifier, ignoreOtherUnitsNegative))
                {
                    net += modifier.Delta * token.Count;
                }
                if (token.ClearTrigger is TokenClearTrigger.FirstTrigger)
                {
                    firstTriggerCount += token.Count;
                }
            }

            // Remove the consumed one-shot grants — and only those. The trigger-aware drain leaves
            // duration grants (ThisActivation / ThisRound) of the same roll kind untouched, so mixing a
            // "once" and a duration buff on one unit no longer clears the duration one early (#033 edge).
            if (firstTriggerCount > 0)
            {
                unit.Tokens.RemoveFirstTriggerTokens(type, firstTriggerCount);
            }
            return net;
        }
    }
}
