namespace FDG.Utilities
{
    /// <summary>
    /// Encapsulates the "alternate teams, round-robin within team, skipping anyone
    /// out of work" pattern shared by deployment, round activations, and objective
    /// placement. State is plain data (team order + per-team current player index)
    /// so it survives save/load. The "does this team/player still have work to do?"
    /// predicates are passed in at advance time rather than stored, so the cursor
    /// doesn't carry delegates that could go stale.
    /// </summary>
    public class TeamPlayerAlternationCursor
    {
        public IReadOnlyList<ITeam> TeamOrder { get; }

        public int CurrentTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentPlayerIndexPerTeam { get; }

        public TeamPlayerAlternationCursor(IReadOnlyList<ITeam> teamOrder)
        {
            if (teamOrder == null || teamOrder.Count == 0)
                throw new ArgumentException("Team order must contain at least one team.", nameof(teamOrder));

            TeamOrder = teamOrder;
            CurrentTeamIndex = 0;
            CurrentPlayerIndexPerTeam = new Dictionary<ITeam, int>();
            foreach (var team in teamOrder)
                CurrentPlayerIndexPerTeam[team] = 0;
        }

        public ITeam GetCurrentTeam() => TeamOrder[CurrentTeamIndex];

        public PlayerID GetCurrentPlayerID()
        {
            var team = GetCurrentTeam();
            int playerIndex = CurrentPlayerIndexPerTeam[team];
            return team.Players[playerIndex];
        }

        /// <summary>
        /// Advances the cursor to the next (team, player) that still has work to do,
        /// alternating teams and round-robining within each team. Returns false if
        /// no team has remaining work, in which case the cursor is unchanged.
        /// </summary>
        public bool TryAdvance(
            Func<ITeam, bool> teamHasRemainingWork,
            Func<PlayerID, bool> playerHasRemainingWork,
            out ITeam? nextTeam,
            out PlayerID? nextPlayerID)
        {
            int teamCount = TeamOrder.Count;
            int firstTeamToCheck = (CurrentTeamIndex + 1) % teamCount;

            int nextTeamIndex = firstTeamToCheck;
            while (true)
            {
                var candidate = TeamOrder[nextTeamIndex];
                if (teamHasRemainingWork(candidate))
                {
                    CurrentTeamIndex = nextTeamIndex;
                    nextTeam = candidate;
                    nextPlayerID = AdvancePlayerWithinTeam(candidate, playerHasRemainingWork);
                    return true;
                }

                nextTeamIndex = (nextTeamIndex + 1) % teamCount;
                if (nextTeamIndex == firstTeamToCheck)
                {
                    nextTeam = null;
                    nextPlayerID = null;
                    return false;
                }
            }
        }

        private PlayerID AdvancePlayerWithinTeam(ITeam team, Func<PlayerID, bool> playerHasRemainingWork)
        {
            if (team.Players.Count == 1)
                return team.Players[0];

            int playerCount = team.Players.Count;
            int startingIndex = CurrentPlayerIndexPerTeam[team];
            int firstPlayerToCheck = (startingIndex + 1) % playerCount;

            int nextPlayerIndex = firstPlayerToCheck;
            while (true)
            {
                var candidate = team.Players[nextPlayerIndex];
                if (playerHasRemainingWork(candidate))
                {
                    CurrentPlayerIndexPerTeam[team] = nextPlayerIndex;
                    return candidate;
                }

                nextPlayerIndex = (nextPlayerIndex + 1) % playerCount;
                if (nextPlayerIndex == firstPlayerToCheck)
                    throw new InvalidOperationException(
                        $"Team {team.TeamNumber} reported remaining work but no player on it has any. " +
                        $"Check that the team-level predicate aggregates the player-level predicate correctly.");
            }
        }
    }
}
