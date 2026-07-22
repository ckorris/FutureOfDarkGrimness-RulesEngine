using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Reads — and, for the spendable kinds, prompts the attacking player to consume — the
    /// attacker-bonus markers sitting on a defender (#100 #14b, the Tag/Target/Spotter family).
    /// Each marker is worth +1 to the attacker for the roll kind its token type names:
    /// <list type="bullet">
    ///   <item>Persistent markers (the Target family) are counted every attack and never removed.</item>
    ///   <item>Spendable markers (Tag/Spotter) are claimed before the roll: the ATTACKING player is
    ///         asked how many to remove ("may remove markers from their target"), and each removed
    ///         marker buys +1 for this roll only. The prompt is skipped when there is nothing to
    ///         spend; the options are ordered spend-all first, so the CLI EOF default and the AI's
    ///         first-option fallback both take the aggressive default.</item>
    /// </list>
    /// The roll stages fold the returned net with their own sign conventions: the hit stage lowers
    /// <c>HitRollNeeded</c> by it, the save stage raises the defender's threshold by it.
    /// </summary>
    public static class TargetMarkerSpend
    {
        /// <summary>
        /// Net attacker bonus from the defender's markers for <paramref name="roll"/>
        /// (<see cref="ERollKind.Hit"/> or <see cref="ERollKind.Save"/>): persistent count plus
        /// whatever spendable markers the attacking player chooses to remove now.
        /// </summary>
        public static async Task<int> ConsumeNet(IGameContext gameContext, PlayerID attackingPlayer,
            IUnit defender, ERollKind roll)
        {
            (TokenType persistent, TokenType spendable, string bonusText) = roll switch
            {
                ERollKind.Hit => (TokenType.PersistentHitBonus, TokenType.SpendableHitBonus, "+1 to hit"),
                ERollKind.Save => (TokenType.PersistentApBonus, TokenType.SpendableApBonus, "AP(+1)"),
                _ => throw new ArgumentOutOfRangeException(nameof(roll), roll,
                    "Attacker-bonus markers exist only for hit and save rolls."),
            };

            int net = defender.Tokens.GetTokenCount(persistent);

            int available = defender.Tokens.GetTokenCount(spendable);
            if (available > 0)
            {
                int spent = await AskSpendCount(gameContext, attackingPlayer, defender, available, bonusText);
                if (spent > 0)
                {
                    defender.Tokens.RemoveTokens(spendable, spent);
                    gameContext.Log($"Spent {spent} marker(s) on {defender.Name}: {bonusText} x{spent}.");
                }

                net += spent;
            }

            return net;
        }

        private static async Task<int> AskSpendCount(IGameContext gameContext, PlayerID attackingPlayer,
            IUnit defender, int available, string bonusText)
        {
            // Spend-all first: the aggressive choice is the automated default (CLI EOF / AI fallback).
            List<int> counts = Enumerable.Range(0, available + 1).Select(i => available - i).ToList();
            List<string> options = counts
                .Select(c => c == 0 ? "Spend 0 markers" : $"Spend {c} marker(s): {bonusText} x{c}")
                .ToList();

            StringSelectionRequest request = new StringSelectionRequest(attackingPlayer,
                $"{defender.Name} carries {available} spendable marker(s) ({bonusText} each). Remove how many?",
                options, new List<StringSelectionRequest.InvalidOption>());
            string choice = await gameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            int index = options.IndexOf(choice);
            return index >= 0 ? counts[index] : available;
        }
    }
}
