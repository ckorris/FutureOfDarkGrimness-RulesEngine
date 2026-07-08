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
        public static int ConsumeNet(IUnit unit, ERollKind roll)
        {
            TokenType type = RollModifierTokens.TypeFor(roll);

            int net = 0;
            int firstTriggerCount = 0;
            foreach (Token token in unit.Tokens.GetAllTokens(type).ToList())
            {
                if (token.Payload is TokenPayload.StatModifier modifier)
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
