using Newtonsoft.Json;

namespace FDG
{
    public interface ITerrain : IZone
    {
        ETerrainType TerrainType { get; }

        IZone Shape { get; }

        float HeightInches { get; }

        /// <summary>
        /// What this piece does to a sight line drawn between two points. The default
        /// implementation on <see cref="TerrainData"/> reads <see cref="TerrainType"/>
        /// flags and queries <see cref="Shape"/>; custom terrain (doors, fog, mesh
        /// colliders, etc.) can override to inject state-dependent or richer behavior.
        /// </summary>
        ESightLineEffect EvaluateSightLine(Position attacker, Position target);
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

        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
            => Shape.GetFirstSegmentEntry(startPosition, endPosition);

        public virtual ESightLineEffect EvaluateSightLine(Position attacker, Position target)
        {
            //2D footprint query. Height is intentionally ignored for now — when
            //heightmap-aware sight lines land, override this method on a height-aware
            //TerrainData subclass rather than complicating the default.
            Float2 a = new Float2(attacker.x, attacker.z);
            Float2 b = new Float2(target.x, target.z);

            if (TerrainType.HasFlag(ETerrainType.Blocking) && Shape.DoesPathIntersectZone(a, b))
            {
                return ESightLineEffect.Blocking;
            }
            if (TerrainType.HasFlag(ETerrainType.Cover) && Shape.DoesPathIntersectZone(a, b))
            {
                return ESightLineEffect.Cover;
            }
            return ESightLineEffect.Clear;
        }
    }
}
