using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #426: the turn order for "players alternate in placing X units, starting with the player that activates
    /// next" - the clause Scout and Ambush share in both 3.5 and Core Rules v3.6.0, which the engine did not
    /// implement (Scouts landed in list order, Ambush arrivals in army-store order).
    ///
    /// <para>The order is the round's own activation order, walked with the same
    /// <see cref="TeamPlayerAlternationCursor"/> the activation loop uses (teams alternate, players
    /// round-robin within a team, anyone out of work is skipped), parked before the first turn exactly like
    /// <c>SingleRoundContext</c>. So "the player that activates next" is by construction the one the next
    /// activation would go to: the deployment roll order's head after deployment (round 1 seeds its order from
    /// it), this round's <c>TeamActivateOrder</c> head at a round start.</para>
    ///
    /// <para>Each turn is ONE unit's worth of work, which the caller's <c>takeTurn</c> must consume (place a
    /// unit, or retire the player's remaining offers) - that is what makes the walk terminate. A participant
    /// on none of the listed teams (test harnesses that build no teams) still gets its turns, after the
    /// teams', round-robin in the order given, so nothing is ever silently left unplaced.</para>
    /// </summary>
    public static class PlacementAlternation
    {
        public static async Task RunAsync(IReadOnlyList<ITeam> activationOrder,
            IReadOnlyList<PlayerID> participants, Func<PlayerID, bool> hasWork, Func<PlayerID, Task> takeTurn)
        {
            var onATeam = new HashSet<PlayerID>();
            if (activationOrder.Count > 0)
            {
                foreach (ITeam team in activationOrder)
                {
                    foreach (PlayerID player in team.Players) onATeam.Add(player);
                }

                var cursor = new TeamPlayerAlternationCursor(activationOrder);
                cursor.ParkBeforeFirstTurn();
                while (cursor.TryAdvance(team => team.Players.Any(hasWork), hasWork, out _, out PlayerID? next))
                {
                    await takeTurn(next!.Value);
                }
            }

            List<PlayerID> strays = participants.Where(p => !onATeam.Contains(p)).Distinct().ToList();
            while (strays.Any(hasWork))
            {
                foreach (PlayerID player in strays)
                {
                    if (hasWork(player)) await takeTurn(player);
                }
            }
        }
    }
}
