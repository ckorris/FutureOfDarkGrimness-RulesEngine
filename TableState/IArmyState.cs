using FDG.Data;

namespace FDG
{
    public interface IArmyState
    {
        public IEnumerable<IArmy> PlayerArmies { get; }
    }

    public class ArmyState : IArmyState
    {
        public IEnumerable<IArmy> PlayerArmies => _gameDataStore.GetAllValues<ArmyData>();

        private IReadableGameDataStore _gameDataStore;

        public ArmyState(IReadableGameDataStore gameDataStore)
        {
            _gameDataStore = gameDataStore;
        }
    }
}