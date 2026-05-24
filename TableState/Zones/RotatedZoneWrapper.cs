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
    }
}
