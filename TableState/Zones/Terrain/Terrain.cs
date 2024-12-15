
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

        public bool IsPointWithinZone(Position position)
        {
            throw new System.NotImplementedException();
        }

        public bool DoesPathIntersectZone(Position startPosition, Position endPosition)
        {
            throw new System.NotImplementedException();
        }
    }
}
