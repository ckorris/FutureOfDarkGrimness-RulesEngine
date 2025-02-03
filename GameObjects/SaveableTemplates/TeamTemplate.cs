
namespace FDG
{
    public class TeamTemplate : ITeam //TODO: Switch to template interface, like other data?
    {
        public int TeamNumber { get; }

        public IReadOnlyList<IPlayerInfo> Players { get; }


        public TeamTemplate(int teamNumber, List<IPlayerInfo> players)
        {
            TeamNumber = teamNumber;
            Players = players;
        }
    }
}
