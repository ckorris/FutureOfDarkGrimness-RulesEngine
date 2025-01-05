
using FDG.Data;
using System.Linq;

namespace FDG
{

    public interface ITerrainState
    {
        public IEnumerable<ITerrain> Terrain { get; }
    }

    public class TerrainState : ITerrainState
    {
        public IEnumerable<ITerrain> Terrain => _gameDataStore.GetAllValues<Terrain>().Cast<ITerrain>();

        private IReadableGameDataStore _gameDataStore;

        public TerrainState(IReadableGameDataStore gameDataStore)
        {
            _gameDataStore = gameDataStore;
        }

    }
}
