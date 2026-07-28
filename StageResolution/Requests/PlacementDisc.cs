namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// A circular constraint region carried BY VALUE on a <see cref="PlaceObjectsRequest{T}"/> (#197 P22),
    /// snapshotted at request build so the request fully describes its own constraints — nothing on the
    /// table moves while a placement is being resolved, so the snapshot cannot go stale. Two roles,
    /// distinguished by which list holds the disc:
    /// <list type="bullet">
    /// <item><b>Keep-out</b> (<see cref="PlaceObjectsRequest{T}.EnemyKeepOutDiscs"/>): a placed model's
    /// centre must land strictly OUTSIDE the disc (Repel Ambushers' 12" per enemy model).</item>
    /// <item><b>Waiver</b> (<see cref="PlaceObjectsRequest{T}.EnemyDistanceWaiverDiscs"/>): a placed
    /// model's centre landing INSIDE the disc ignores every enemy-distance restriction — the generic
    /// <see cref="PlaceObjectsRequest{T}.MinDistanceFromEnemiesInches"/> and the keep-out discs alike
    /// (Ambush Beacon's 6"; owner-ruled per-model, waives both).</item>
    /// </list>
    /// Distances are centre-to-centre in table inches, matching the existing enemy-distance rule
    /// ("base-edge ignored; center distance").
    /// </summary>
    public record PlacementDisc(Position Center, float RadiusInches);
}
