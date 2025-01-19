
namespace FDG
{
    public interface ITerrain : IZone
    {
        public ETerrainType TerrainType { get; }
    }

    public struct Terrain : ITerrain
    {
        public ETerrainType TerrainType { get; private set; }

        public Terrain(ETerrainType terrainType)
        {

        }

        public bool IsPointWithinZone(Float2 position)
        {
            throw new NotImplementedException();
        }

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
        {
            throw new System.NotImplementedException();
        }
    }
}
