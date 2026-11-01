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
    ///   <item>#434: 3.6 Tag tokens (<see cref="TokenType.SingleSpendHitBonus"/> / <see cref="TokenType.SingleSpendApBonus"/>)
    ///         are claimed one per attack: the attacking player may remove ONE for +1.</item>
    ///   <item>Spendable markers (the Spotters; 3.5 Tags) are claimed before the roll: the ATTACKING player is
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
            (TokenType persistent, TokenType spendable, TokenType singleSpend, string bonusText) = roll switch
            {
                ERollKind.Hit => (TokenType.PersistentHitBonus, TokenType.SpendableHitBonus,
                    TokenType.SingleSpendHitBonus, "+1 to hit"),
                ERollKind.Save => (TokenType.PersistentApBonus, TokenType.SpendableApBonus,
                    TokenType.SingleSpendApBonus, "AP(+1)"),
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

            // #434: 3.6 Tag tokens - "may remove ONE token from their target" per attack, for +1.
            if (defender.Tokens.GetTokenCount(singleSpend) > 0
                && await AskSpendOne(gameContext, attackingPlayer, defender, singleSpend, bonusText))
            {
                defender.Tokens.RemoveTokens(singleSpend, 1);
                gameContext.Log($"Removed a Tag token from {defender.Name}: {bonusText}.");
                net += 1;
            }

            return net;
        }

        private static async Task<bool> AskSpendOne(IGameContext gameContext, PlayerID attackingPlayer,
            IUnit defender, TokenType singleSpend, string bonusText)
        {
            int available = defender.Tokens.GetTokenCount(singleSpend);
            // Spend first: the aggressive choice is the automated default (CLI EOF / AI fallback), as above.
            string spend = $"Remove 1 Tag token: {bonusText}";
            var options = new List<string> { spend, "Remove none" };
            StringSelectionRequest request = new StringSelectionRequest(attackingPlayer,
                $"{defender.Name} carries {available} Tag token(s). Remove one for {bonusText} on this attack?",
                options, new List<StringSelectionRequest.InvalidOption>(),
                displayName: "Spending a Tag Token");
            string choice = await gameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);
            return choice != "Remove none";
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
                options, new List<StringSelectionRequest.InvalidOption>(),
                displayName: "Spending Target Markers");
            string choice = await gameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            int index = options.IndexOf(choice);
            return index >= 0 ? counts[index] : available;
        }
    }
}
