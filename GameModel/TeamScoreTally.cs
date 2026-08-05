using FDG.Players;

namespace FDG
{
    /// <summary>A team's pooled objective count, and who is on it.</summary>
    /// <param name="TeamNumber">The slot team number, or a negative pseudo-team for an objective owner
    /// that holds no player slot (see <see cref="TeamScoreTally.Build"/>).</param>
    /// <param name="Players">Every player on the team, in slot order; for a pseudo-team, the single
    /// unslotted objective owner it was created for. Never empty.</param>
    /// <param name="ObjectiveCount">Objectives held by the team, summed over its players (#257).</param>
    public readonly record struct TeamScore(int TeamNumber, IReadOnlyList<PlayerID> Players, int ObjectiveCount);

    /// <summary>
    /// The #257 team-pooled objective tally: who is winning, by the same arithmetic that decides the game.
    ///
    /// <para>
    /// Extracted from <see cref="Stages.VictoryCalculationStage"/> for #331, so the front end can colour a
    /// victory celebration by the winning side without recomputing the tally and risking a different answer
    /// than the engine's. It also closes the gap that made that necessary: the structured
    /// <see cref="GameResult"/> is host-side only and never crosses the wire, so a networked client cannot
    /// be told who won - but the objectives and player slots it reads here ARE replicated, so every machine
    /// derives the same winner from the same state.
    /// </para>
    /// </summary>
    public static class TeamScoreTally
    {
        /// <summary>
        /// Pooled scores, one entry per team that holds at least one objective, highest first (ties keep
        /// team-registration order, so the result is deterministic). Teams holding nothing are omitted -
        /// callers asking "who is winning" never want a wall of zeroes - so an empty result means no
        /// objective is owned by anyone.
        /// </summary>
        public static IReadOnlyList<TeamScore> Build(IEnumerable<IPlayerSlotInfo> players,
            IEnumerable<IObjective> objectives)
        {
            List<IPlayerSlotInfo> slots = players.ToList();

            var teamOfPlayer = new Dictionary<PlayerID, int>();
            foreach (IPlayerSlotInfo slot in slots)
                teamOfPlayer[slot.PlayerID] = slot.TeamNumber;

            // An objective owner with no slot keeps a private bucket. The pseudo keys count down from
            // int.MinValue so they can never collide with a real team number, and they mirror the old
            // per-player behaviour for that edge rather than silently merging those owners together.
            int nextPseudoTeam = int.MinValue;
            var countByTeam = new Dictionary<int, int>();
            var order = new List<int>();
            var pseudoTeamOwner = new Dictionary<int, PlayerID>();

            foreach (IObjective objective in objectives) // deterministic order; covers every owner.
            {
                if (!objective.OwnerID.HasValue) continue;
                PlayerID owner = objective.OwnerID.Value;
                if (!teamOfPlayer.TryGetValue(owner, out int team))
                {
                    teamOfPlayer[owner] = team = nextPseudoTeam++;
                    pseudoTeamOwner[team] = owner;
                }

                if (!countByTeam.ContainsKey(team)) order.Add(team);
                countByTeam.TryGetValue(team, out int current);
                countByTeam[team] = current + 1;
            }

            // Registration order first, so OrderByDescending (a stable sort) leaves equal scores in a
            // deterministic sequence rather than dictionary order.
            var byRegistration = new List<int>();
            foreach (IPlayerSlotInfo slot in slots.OrderBy(slot => slot.SlotID))
                if (countByTeam.ContainsKey(slot.TeamNumber) && !byRegistration.Contains(slot.TeamNumber))
                    byRegistration.Add(slot.TeamNumber);
            foreach (int team in order)
                if (!byRegistration.Contains(team)) byRegistration.Add(team);

            var scores = new List<TeamScore>();
            foreach (int team in byRegistration)
            {
                List<PlayerID> roster = slots
                    .Where(slot => slot.TeamNumber == team)
                    .OrderBy(slot => slot.SlotID)
                    .Select(slot => slot.PlayerID)
                    .ToList();

                // A pseudo-team has no slots, so its "roster" is the single unslotted owner that created
                // it. That keeps Players meaningful for every entry, and spares callers a null branch.
                if (roster.Count == 0 && pseudoTeamOwner.TryGetValue(team, out PlayerID solo))
                    roster.Add(solo);

                scores.Add(new TeamScore(team, roster, countByTeam[team]));
            }

            return scores.OrderByDescending(score => score.ObjectiveCount).ToList();
        }

        /// <summary>
        /// The teams tied at the top of <paramref name="scores"/>: one entry is an outright winner, more
        /// than one is a tie between them, and none means nobody holds an objective. Callers that need a
        /// single winner treat anything but a count of 1 as a tie, which is what the engine does.
        /// </summary>
        public static IReadOnlyList<TeamScore> TopTeams(IReadOnlyList<TeamScore> scores)
        {
            if (scores.Count == 0) return Array.Empty<TeamScore>();
            int top = scores.Max(score => score.ObjectiveCount);
            if (top == 0) return Array.Empty<TeamScore>();
            return scores.Where(score => score.ObjectiveCount == top).ToList();
        }

        /// <summary>Convenience overload reading straight off the table state - the front-end entry point.</summary>
        public static IReadOnlyList<TeamScore> Build(ITableState tableState) =>
            Build(tableState.Players.Objects.Where(slot => slot.IsFilled), tableState.Objectives.Objects);
    }
}
