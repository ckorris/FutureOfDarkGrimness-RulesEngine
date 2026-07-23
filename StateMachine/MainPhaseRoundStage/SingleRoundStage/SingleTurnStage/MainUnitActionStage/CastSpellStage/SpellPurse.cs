using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P23 — everything a caster may spend as a spell token right now: its OWN
    /// <see cref="TokenType.SpellTokens"/> plus every pool a nearby friendly unit is lending it
    /// (<c>Spell Accumulator</c>: "casters from other friendly units within 12" may spend this model's
    /// accumulator tokens as if they were their own spell tokens").
    ///
    /// <para>One place, because "as if they were their own spell tokens" means EVERY use a caster has for a
    /// spell token: paying a spell's cost, boosting its own cast roll (#244), and swaying someone else's
    /// cast (#103). Each of those used to read <c>unit.Tokens.GetTokenCount(SpellTokens)</c> directly; they
    /// now ask here, so a borrowed token is spendable everywhere an owned one is and the three sites cannot
    /// drift apart. The AI's affordability checks read the same purse.</para>
    ///
    /// <para><b>Own tokens are spent first, then lenders in table order.</b> A caster's own tokens are
    /// usable by nobody else, while a lender's pool is shared with every friendly caster in its range, so
    /// draining the restricted resource first leaves the team the most options. It also means a caster who
    /// can already afford a spell behaves exactly as it did before any accumulator existed. Table order
    /// among lenders is arbitrary but deterministic; nothing in the corpus distinguishes them.</para>
    ///
    /// <para>Who lends is NOT read off the token type: it is asked at
    /// <see cref="EHookID.Lifecycle_OnCapabilityQuery"/> and answered by whatever rule confers lending, so
    /// the "only if this unit isn't Shaken" clause is a plain <see cref="Condition"/> on that entry and a
    /// second lending rule needs no change here. See <see cref="CapabilityRuleQueries.LentPools"/>.</para>
    /// </summary>
    public static class SpellPurse
    {
        /// <summary>One lender's contribution: whose pool, which pool, and how many tokens.</summary>
        public readonly record struct Loan(IUnit Lender, TokenType Pool, int Count);

        /// <summary>
        /// Total spendable as spell tokens by <paramref name="caster"/> right now — its own pool plus every
        /// loan on offer. This is the number every affordability check should compare a spell's cost against.
        /// </summary>
        public static int Available(ITableState tableState, RuleEvaluator evaluator, IUnit caster)
        {
            int total = caster.Tokens.GetTokenCount(TokenType.SpellTokens);
            foreach (Loan loan in Offered(tableState, evaluator, caster))
            {
                total += loan.Count;
            }

            return total;
        }

        /// <summary>
        /// The loans available to <paramref name="caster"/>, in the order they would be drawn on. A lender
        /// qualifies when it is a living, on-table, friendly OTHER unit that answers the capability query
        /// with a pool, is within that pool's range, and actually holds tokens in it.
        ///
        /// <para>"Other" is the rule's word and is load-bearing: an accumulator that is itself a caster (the
        /// corpus does put Change Boon on caster units) may not draw on its own pool.</para>
        /// </summary>
        public static IReadOnlyList<Loan> Offered(ITableState tableState, RuleEvaluator evaluator,
            IUnit caster)
        {
            List<Loan>? loans = null;

            foreach (IUnit lender in tableState.Units.Objects)
            {
                if (ReferenceEquals(lender, caster) || lender.ID.Equals(caster.ID)) continue;
                if (!lender.GetIsAlive() || !lender.GetIsOnBattlefield()) continue;
                if (!IsFriendly(tableState, caster.PlayerID, lender.PlayerID)) continue;

                IReadOnlyList<RuleOperation.EnableSpellLending> pools =
                    CapabilityRuleQueries.LentPools(lender, evaluator);
                if (pools.Count == 0) continue;

                // Only pay for the distance once a unit has actually offered something.
                float distance = UnitCompareUtilities.MinDistanceBetweenUnits(
                    caster, lender, out _, out _, includeVertical: true);

                foreach (RuleOperation.EnableSpellLending pool in pools)
                {
                    if (distance > pool.RangeInches) continue;

                    int count = lender.Tokens.GetTokenCount(pool.Pool);
                    if (count <= 0) continue;

                    (loans ??= new List<Loan>()).Add(new Loan(lender, pool.Pool, count));
                }
            }

            return (IReadOnlyList<Loan>?)loans ?? System.Array.Empty<Loan>();
        }

        /// <summary>
        /// Spends <paramref name="amount"/> from <paramref name="caster"/>'s purse — its own tokens first,
        /// then lenders in <see cref="Offered"/> order — and returns the loans actually drawn on (empty when
        /// the caster paid entirely from its own pool, which is the overwhelmingly common case).
        ///
        /// <para>Spends what is there and stops. Callers gate on <see cref="Available"/> first, so a short
        /// purse means the caller's own bookkeeping is wrong rather than the player's; taking less is the
        /// non-destructive failure. Re-reads the loans at spend time, so two casters drawing on one pool in
        /// the same cast see each other's spending.</para>
        /// </summary>
        public static IReadOnlyList<Loan> Spend(ITableState tableState, RuleEvaluator evaluator,
            IUnit caster, int amount)
        {
            if (amount <= 0) return System.Array.Empty<Loan>();

            int fromOwn = System.Math.Min(amount, caster.Tokens.GetTokenCount(TokenType.SpellTokens));
            if (fromOwn > 0)
            {
                caster.Tokens.RemoveTokens(TokenType.SpellTokens, fromOwn);
                amount -= fromOwn;
            }

            if (amount <= 0) return System.Array.Empty<Loan>();

            List<Loan> drawn = new List<Loan>();
            foreach (Loan loan in Offered(tableState, evaluator, caster))
            {
                int taken = System.Math.Min(amount, loan.Count);
                loan.Lender.Tokens.RemoveTokens(loan.Pool, taken);
                drawn.Add(loan with { Count = taken });

                amount -= taken;
                if (amount <= 0) break;
            }

            return drawn;
        }

        /// <summary>
        /// "Cabal of Change lends 2, Change Host lends 1" — the borrowed half of a spend, for the log line
        /// the caller builds. Empty string when nothing was borrowed, so callers can append it unconditionally.
        /// </summary>
        public static string Describe(IReadOnlyList<Loan> loans)
        {
            if (loans.Count == 0) return "";

            List<string> parts = new List<string>();
            foreach (Loan loan in loans)
            {
                parts.Add($"{loan.Lender.Name} lends {loan.Count}");
            }

            return string.Join(", ", parts);
        }

        // Team-aware friendliness, mirroring SpellTargeting and SpellValuation: teammates count as friendly;
        // with no registered team, only the player itself does.
        private static bool IsFriendly(ITableState tableState, PlayerID player, PlayerID other)
        {
            foreach (ITeam team in tableState.Teams.Objects)
            {
                if (team.IsPlayerOnTeam(player)) return team.IsPlayerOnTeam(other);
            }

            return player == other;
        }
    }
}
