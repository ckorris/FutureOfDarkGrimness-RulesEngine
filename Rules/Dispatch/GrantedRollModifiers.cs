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

            // Remove the consumed one-shot grants. Owner-agnostic drain: in the dominant case a unit holds a
            // single roll-modifier buff of a given kind; mixing a "once" and a duration grant of the SAME
            // roll on one unit is the rare edge that could clear the duration one early (recorded in #033).
            if (firstTriggerCount > 0)
            {
                unit.Tokens.RemoveTokens(type, firstTriggerCount);
            }
            return net;
        }
    }
}
