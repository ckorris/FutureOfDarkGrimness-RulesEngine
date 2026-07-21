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

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
        {
            foreach (var part in Parts)
                if (part.DoesPathIntersectZone(startPosition, endPosition, inflationRadius)) return true;
            return false;
        }

        /// <summary>
        /// Returns the nearest segment-entry point across all parts, or null if no
        /// part is entered.
        /// </summary>
        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
        {
            Float2? best = null;
            float bestDistSq = float.MaxValue;
            foreach (var part in Parts)
            {
                Float2? entry = part.GetFirstSegmentEntry(startPosition, endPosition);
                if (!entry.HasValue) continue;
                float dx = entry.Value.X - startPosition.X;
                float dy = entry.Value.Y - startPosition.Y;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = entry;
                }
            }
            return best;
        }

        /// <summary>
        /// Returns the farthest segment-exit point across all parts. When the end is
        /// outside every part this IS the union's last exit: if the segment were still
        /// inside some other part just after the farthest per-part exit, that part's own
        /// exit would be even farther, contradicting maximality. Null if the end is
        /// inside any part or no part is crossed.
        /// </summary>
        public Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition)
        {
            if (IsPointWithinZone(endPosition)) return null;

            Float2? best = null;
            float bestDistSq = -1f;
            foreach (var part in Parts)
            {
                Float2? exit = part.GetLastSegmentExit(startPosition, endPosition);
                if (!exit.HasValue) continue;
                float dx = exit.Value.X - startPosition.X;
                float dy = exit.Value.Y - startPosition.Y;
                float distSq = dx * dx + dy * dy;
                if (distSq > bestDistSq)
                {
                    bestDistSq = distSq;
                    best = exit;
                }
            }
            return best;
        }
    }
}
