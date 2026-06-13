using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// Rotates any <see cref="IZone"/> by a fixed angle around a pivot point.
    /// Membership and path-intersection delegate to the inner zone after
    /// inverse-rotating the query point/path into the inner zone's local frame.
    ///
    /// Circles are rotation-invariant about their own center, so wrapping a circle
    /// with a non-coincident pivot is the same as translating the circle to the
    /// rotated center. The <see cref="ZoneExtensions.Primitives"/> walker
    /// flattens that case into a plain <see cref="CircularZone"/>, so the only
    /// rotated primitive that ever leaks out to downstream math is a rotated
    /// rectangle (an OBB).
    /// </summary>
    public class RotatedZoneWrapper : IZone
    {
        public IZone Inner { get; }
        public float AngleDegrees { get; }
        public Float2 Pivot { get; }

        [JsonConstructor]
        public RotatedZoneWrapper(IZone inner, float angleDegrees, Float2 pivot)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            Inner = inner;
            AngleDegrees = angleDegrees;
            Pivot = pivot;
        }

        public bool IsPointWithinZone(Float2 position)
            => Inner.IsPointWithinZone(ZoneExtensions.RotateAround(position, Pivot, -AngleDegrees));

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
            => Inner.DoesPathIntersectZone(
                ZoneExtensions.RotateAround(startPosition, Pivot, -AngleDegrees),
                ZoneExtensions.RotateAround(endPosition, Pivot, -AngleDegrees));

        //Rotation about the pivot is rigid (distance-preserving), so the swept-disc radius carries
        //through unchanged; only the endpoints are inverse-rotated into the inner zone's local frame.
        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
            => Inner.DoesPathIntersectZone(
                ZoneExtensions.RotateAround(startPosition, Pivot, -AngleDegrees),
                ZoneExtensions.RotateAround(endPosition, Pivot, -AngleDegrees),
                inflationRadius);

        /// <summary>
        /// Inverse-rotates the segment into the inner zone's local frame, delegates,
        /// then forward-rotates the entry point back to world space.
        /// </summary>
        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
        {
            Float2 localStart = ZoneExtensions.RotateAround(startPosition, Pivot, -AngleDegrees);
            Float2 localEnd   = ZoneExtensions.RotateAround(endPosition,   Pivot, -AngleDegrees);
            Float2? localEntry = Inner.GetFirstSegmentEntry(localStart, localEnd);
            if (!localEntry.HasValue) return null;
            return ZoneExtensions.RotateAround(localEntry.Value, Pivot, AngleDegrees);
        }
    }
}
