using Newtonsoft.Json;

namespace FDG
{
    public interface ITerrain : IZone
    {
        ETerrainType TerrainType { get; }

        IZone Shape { get; }

        float HeightInches { get; }
    }

    public class TerrainData : ITerrain
    {
        public ETerrainType TerrainType { get; }

        public IZone Shape { get; }

        public float HeightInches { get; }

        [JsonConstructor]
        public TerrainData(ETerrainType terrainType, IZone shape, float heightInches)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            TerrainType = terrainType;
            Shape = shape;
            HeightInches = heightInches;
        }

        public TerrainData(ETerrainType terrainType, IZone shape) : this(terrainType, shape, 0f) { }

        public bool IsPointWithinZone(Float2 position) => Shape.IsPointWithinZone(position);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
            => Shape.DoesPathIntersectZone(startPosition, endPosition);
    }
}
