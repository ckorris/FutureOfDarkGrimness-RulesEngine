namespace FDG
{
    /// <summary>
    /// An <see cref="IZone"/> with a known axis-aligned bounding box — the subset of zones usable as a
    /// placement region (deployment, Scout/Ambush arrival, transport disembark). The model-placement
    /// resolvers take an <see cref="IBoundedZone"/> so they can scan <see cref="Bounds"/> for candidate
    /// positions and filter them by the zone's true shape (<see cref="IZone.IsPointWithinZone"/>) — letting
    /// a placement be any bounded shape (a rectangle or a circle today) without the resolvers knowing which.
    /// Unbounded / wrapper zones (composite, rotated, terrain footprints) are never placement targets, so
    /// they stay plain <see cref="IZone"/>.
    /// </summary>
    public interface IBoundedZone : IZone
    {
        ZoneBounds Bounds { get; }
    }
}
