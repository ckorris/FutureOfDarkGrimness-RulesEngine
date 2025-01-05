

using FDG.Data;

namespace FDG
{
    public interface ITableState
    {
        public IPlayerState PlayerState { get; }

        public IArmyState ArmyState { get; }
        
        public ITerrainState TerrainState { get; }
    }

    public class TableState : ITableState
    {
        public IPlayerState PlayerState { get; private set; }

        public IArmyState ArmyState { get; private set; }

        public ITerrainState TerrainState { get; private set; }

        public TableState(IReadableGameDataStore gameDataStore)
        {
            PlayerState = new PlayerState(gameDataStore);
            ArmyState = new ArmyState(gameDataStore);
            TerrainState = new TerrainState(gameDataStore);
        }
    }
}
