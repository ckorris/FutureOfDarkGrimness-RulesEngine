

namespace FDG
{

    public interface ITerrainState
    {
        public IReadOnlyList<ITerrain> Terrain { get; }

        public void AddTerrain(ITerrain newTerrain);
    }

    public class TerrainState : ITerrainState
    {
        public IReadOnlyList<ITerrain> Terrain => _terrain;

        private List<ITerrain> _terrain = new List<ITerrain>();

        public void AddTerrain(ITerrain newTerrain)
        {
            _terrain.Add(newTerrain);
        }
    }
}
