
using FDG.Data;
using FDG.Data.Serialization;
using System.Text.Json.Serialization;

namespace FDG
{
    public class TeamData : ITeam, IGameDataAware
    {
        public int TeamNumber { get; private set; }

        public IReadOnlyList<IPlayerInfo> Players
        {
            get
            {
                return _playerBindings.Select(binding => binding.GetValue()).ToList();
            }
        }

        private List<DataReference> _playerReferences;

        private List<DataBinding<PlayerData>> _playerBindings;

        [JsonConstructor]
        public TeamData(int teamNumber, List<DataReference> playerReferences)
        {
            TeamNumber = teamNumber;
            _playerReferences = playerReferences;
        }

        public TeamData(int teamNumber)
        {
            TeamNumber = teamNumber;
            _playerReferences = new List<DataReference>();
            _playerBindings = new List<DataBinding<PlayerData>>();
        }

        public TeamData(int teamNumber, List<DataReference> playerReferences,
            IReadWriteableGameDataStore gameDataStore)
        {
            TeamNumber = teamNumber;

            _playerReferences = playerReferences;

            _playerBindings = new List<DataBinding<PlayerData>>();
            foreach (DataReference playerInfo in playerReferences)
            {
                DataBinding<PlayerData> playerBinding = gameDataStore.GetDataBinding<PlayerData>(playerInfo);
                _playerBindings.Add(playerBinding);
            }
        }

        public void SetGameDataStore(IReadWriteableGameDataStore gameDataStore)
        {
            _playerBindings = new List<DataBinding<PlayerData>>();
            foreach (DataReference playerInfo in _playerReferences)
            {
                DataBinding<PlayerData> playerBinding = gameDataStore.GetDataBinding<PlayerData>(playerInfo);
                _playerBindings.Add(playerBinding);
            }
        }

        public void AddPlayerToTeam(DataReference playerReference)
        {
            _playerReferences.Add(playerReference);
        }


    }
}
