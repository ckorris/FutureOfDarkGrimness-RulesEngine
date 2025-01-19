
namespace FDG
{
    public class TeamTemplate : ITeam //TODO: Switch to template interface, like other data?
    {
        public int TeamNumber { get; }

        public IReadOnlyList<IPlayer> Players { get; }


        public TeamTemplate(int teamNumber, List<IPlayer> players)
        {
            TeamNumber = teamNumber;
            Players = players;
        }
    }
}
