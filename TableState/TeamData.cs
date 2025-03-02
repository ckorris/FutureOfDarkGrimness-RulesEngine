
using FDG.Data;
using System.Text.Json.Serialization;

namespace FDG
{
    public class TeamData : ITeam
    { 
        public int TeamNumber { get; private set; }

        public IReadOnlyList<PlayerID> Players => _players;

        private List<PlayerID> _players;

        [JsonConstructor]
        public TeamData(int teamNumber, List<PlayerID> playerBindings)
        {
            TeamNumber = teamNumber;
            _players = playerBindings;
        }

        public TeamData(int teamNumber)
        {
            TeamNumber = teamNumber;
            _players = new List<PlayerID>();
        }
    }
}
