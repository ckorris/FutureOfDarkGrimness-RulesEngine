
using FDG.Data;
using System.Text.Json.Serialization;

namespace FDG
{
    public class TeamData : ITeam
    { 
        public int TeamNumber { get; private set; }

        public IReadOnlyList<IPlayerInfo> Players
        {
            get
            {
                return PlayerBindings.Select(binding => binding.GetValue()).ToList();
            }
        }

        public List<DataBinding<PlayerData>> PlayerBindings;

        [JsonConstructor]
        public TeamData(int teamNumber, List<DataBinding<PlayerData>> playerBindings)
        {
            TeamNumber = teamNumber;
            PlayerBindings = playerBindings;
        }

        public TeamData(int teamNumber)
        {
            TeamNumber = teamNumber;
            PlayerBindings = new List<DataBinding<PlayerData>>();
        }

        public TeamData(int teamNumber, List<DataReference> playerReferences,
            IReadWriteableGameDataStore gameDataStore)
        {
            TeamNumber = teamNumber;

            PlayerBindings = new List<DataBinding<PlayerData>>();
            foreach (DataReference playerInfo in playerReferences)
            {
                DataBinding<PlayerData> playerBinding = gameDataStore.GetDataBinding<PlayerData>(playerInfo);
                PlayerBindings.Add(playerBinding);
            }
        }
    }
}
