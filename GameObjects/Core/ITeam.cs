
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
    }
}
