
using FDG.Data;

namespace FDG
{
    public class TeamData : ITeam
    {
        public int TeamNumber { get; private set; }

        public IReadOnlyList<IPlayer> Players
        {
            get
            {
                return _playerBindings.Select(binding => binding.GetValue()).ToList();
            }
        }

        private List<DataReference> _playerReferences;

        private List<DataBinding<PlayerData>> _playerBindings;

        public TeamData(int teamNumber)
        {
            TeamNumber = teamNumber;
            _playerReferences = new List<DataReference>();
            _playerBindings = new List<DataBinding<PlayerData>>();
        }

        public TeamData(int teamNumber, List<DataReference> playerReferences,
            IReadWriteableGameDataStore gameDataStore, ICommandProcessor commandProcessor)
        {
            TeamNumber = teamNumber;

            _playerReferences = playerReferences;

            _playerBindings = new List<DataBinding<PlayerData>>();
            foreach (DataReference playerInfo in playerReferences)
            {
                DataBinding<PlayerData> playerBinding = new DataBinding<PlayerData>(commandProcessor,
                    gameDataStore, playerInfo);
                _playerBindings.Add(playerBinding);
            }
        }

        public void AddPlayerToTeam(DataReference playerReference)
        {
            _playerReferences.Add(playerReference);
        }
    }
}
