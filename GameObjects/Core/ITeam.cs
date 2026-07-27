
using System.Runtime.CompilerServices;

namespace FDG
{
    public interface ITeam
    {
        int TeamNumber { get; }

        IReadOnlyList<PlayerID> Players { get; }
    }

    public static class ITeamExtensions
    {
        public static bool IsPlayerOnTeam(this ITeam team, PlayerID player)
        {
            return team.Players.Contains(player);
        }

        /// <summary>
        /// Whether two players are on the same side. The authority for "enemy" everywhere it matters
        /// (<c>MovementUtilities.GetEnemyModelFootprints</c>: "enemies are everyone not on the moving unit's
        /// team"), collected here because the AI resolvers had each written it as <c>PlayerID == us</c> —
        /// which only excludes a player's OWN units, so in a team game an ally counted as hostile.
        /// <para>A player on no team is allied only with itself, which keeps every solo / 1v1 path identical.</para>
        /// </summary>
        public static bool AreAllied(IEnumerable<ITeam> teams, PlayerID a, PlayerID b)
        {
            if (a.Equals(b)) return true;
            ITeam? team = teams.FirstOrDefault(t => t.IsPlayerOnTeam(a));
            return team != null && team.IsPlayerOnTeam(b);
        }

        /// <summary>
        /// The objective owner that end-of-round reconciliation should record, team-aware (#297,
        /// Chris's call: objectives belong to a SIDE - allied players guarding the same marker must
        /// not contest it back to neutral, which the old per-player rule did while victory pools
        /// per team). Exactly one SIDE with players in range holds the marker: the current owner if
        /// they are on that side (ownership is sticky - the original seizer keeps the marker while
        /// a teammate guards it), else the side's first-registered player in range (deterministic,
        /// and the OwnerID stays a plain PlayerID - no data-model change; per-team display is the
        /// #297 UI facet). Several sides contest to null; nobody in range leaves the current owner.
        /// With no registered teams every player is their own side, so 1v1 is unchanged.
        /// </summary>
        public static PlayerID? ReconcileObjectiveOwner(IEnumerable<ITeam> teams,
            PlayerID? currentOwner, IReadOnlyCollection<PlayerID> playersInRange)
        {
            if (playersInRange.Count == 0) return currentOwner;

            List<ITeam> teamList = teams.ToList();
            List<PlayerID> firstOfEachSide = new List<PlayerID>();
            foreach (PlayerID player in playersInRange)
                if (!firstOfEachSide.Any(seen => AreAllied(teamList, seen, player)))
                    firstOfEachSide.Add(player);
            if (firstOfEachSide.Count > 1) return null; // genuinely contested between sides

            if (currentOwner.HasValue && AreAllied(teamList, currentOwner.Value, firstOfEachSide[0]))
                return currentOwner;

            // The seizing side's deterministic representative: team registration order first,
            // falling back to the (single) in-range player when no team is registered.
            ITeam? team = teamList.FirstOrDefault(t => t.IsPlayerOnTeam(firstOfEachSide[0]));
            if (team != null)
                foreach (PlayerID member in team.Players)
                    if (playersInRange.Contains(member))
                        return member;
            return firstOfEachSide[0];
        }
    }
}
