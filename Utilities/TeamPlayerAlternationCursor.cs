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

        /// <summary>
        /// Parks the cursor one step BEFORE the first (team, player) in the order, so that the very first
        /// <see cref="TryAdvance"/> lands ON them instead of past them.
        /// <para>
        /// There are two ways to walk this cursor. Terrain and terrain-points placement READ the current
        /// position and then advance, so for them a fresh cursor pointing at the first turn is exactly right.
        /// The round's activation loop instead advances BEFORE every turn, including the first - and for that
        /// walk a fresh cursor is off by one, because "advance past position 0" hands the opening turn to
        /// whoever is second. Call this once at construction for the second kind of walk.
        /// </para>
        /// <para>
        /// Parking is a plain cursor position (the end of the order, which TryAdvance wraps off), not a flag,
        /// so it survives the save/load round trip through GameProgressData like any other position - a game
        /// saved before a round's first activation resumes with the same player still to lead. ScenarioCompiler
        /// hand-rolls the same parking when it seeds a scenario mid-round.
        /// </para>
        /// </summary>
        public void ParkBeforeFirstTurn()
        {
            CurrentTeamIndex = TeamOrder.Count - 1;
            foreach (ITeam team in TeamOrder)
                CurrentPlayerIndexPerTeam[team] = team.Players.Count - 1;
        }

        public ITeam GetCurrentTeam() => TeamOrder[CurrentTeamIndex];

        public PlayerID GetCurrentPlayerID()
        {
            var team = GetCurrentTeam();
            int playerIndex = CurrentPlayerIndexPerTeam[team];
            return team.Players[playerIndex];
        }

        /// <summary>
        /// Points the cursor directly at <paramref name="playerID"/> instead of advancing, for the cases
        /// where something other than the alternation decides who acts next (#197 P19: a unit flagged to
        /// take the next activation - which may belong to an ALLY, so both the team index and that team's
        /// player index move). Returns false and leaves the cursor untouched if the player is on no team in
        /// this cursor's order, so a caller can fall back to a normal advance.
        /// </summary>
        public bool PointAt(PlayerID playerID)
        {
            for (int teamIndex = 0; teamIndex < TeamOrder.Count; teamIndex++)
            {
                IReadOnlyList<PlayerID> players = TeamOrder[teamIndex].Players;
                int playerIndex = -1;
                for (int i = 0; i < players.Count; i++)
                {
                    if (players[i] == playerID)
                    {
                        playerIndex = i;
                        break;
                    }
                }

                if (playerIndex < 0)
                {
                    continue;
                }

                CurrentTeamIndex = teamIndex;
                CurrentPlayerIndexPerTeam[TeamOrder[teamIndex]] = playerIndex;
                return true;
            }

            return false;
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
