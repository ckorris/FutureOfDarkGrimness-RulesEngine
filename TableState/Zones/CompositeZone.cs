using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// An <see cref="IZone"/> made of one or more child zones. Membership and path
    /// intersection are *union* across parts (any part says true → the composite
    /// says true). Sub-zones inside a composite are not exposed as independent
    /// <see cref="ITerrain"/> entries in <see cref="ITableState.Terrain"/>; the
    /// composite is the only thing the game world sees.
    /// </summary>
    /// <remarks>
    /// Use this for L-shaped buildings, T-shapes, multi-part walls — anything that
    /// can't be expressed as a single rectangle or circle. Nesting is supported:
    /// composites can contain composites; the <see cref="ZoneExtensions.Primitives"/>
    /// walker flattens them.
    /// </remarks>
    public class CompositeZone : IZone
    {
        public IReadOnlyList<IZone> Parts { get; }

        [JsonConstructor]
        public CompositeZone(IReadOnlyList<IZone> parts)
        {
            if (parts == null || parts.Count == 0)
                throw new ArgumentException($"{nameof(CompositeZone)} requires at least one part.", nameof(parts));
            Parts = parts;
        }

        public bool IsPointWithinZone(Float2 position)
        {
            foreach (var part in Parts)
                if (part.IsPointWithinZone(position)) return true;
            return false;
        }

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
        {
            foreach (var part in Parts)
                if (part.DoesPathIntersectZone(startPosition, endPosition)) return true;
            return false;
        }
    }
}
