
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
    }
}
