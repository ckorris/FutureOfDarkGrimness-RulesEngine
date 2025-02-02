
namespace FDG
{
    public class TeamTemplate : ITeam //TODO: Switch to template interface, like other data?
    {
        public int TeamNumber { get; }

        public IReadOnlyList<IPlayerIdentifyable> Players { get; }


        public TeamTemplate(int teamNumber, List<IPlayerIdentifyable> players)
        {
            TeamNumber = teamNumber;
            Players = players;
        }
    }
}
