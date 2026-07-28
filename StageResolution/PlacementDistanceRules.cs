using FDG.StageResolution.Requests;

namespace FDG.StageResolution
{
    /// <summary>
    /// The single authority on a placement request's enemy-distance legality (#197 P22). Every placement
    /// resolver (CLI, GUI, solo-AI, Tactician) used to test only the flat
    /// <see cref="PlaceObjectsRequest{T}.MinDistanceFromEnemiesInches"/>; with Repel Ambushers adding
    /// per-source keep-out discs and Ambush Beacon adding waiver discs that override BOTH restriction
    /// kinds, the combination logic lives here so four resolvers cannot drift apart. Resolvers keep
    /// their own (team-aware) enemy scans — they own the table-state access — and hand the positions in.
    /// </summary>
    public static class PlacementDistanceRules
    {
        /// <summary>
        /// Whether <paramref name="candidate"/> sits inside a waiver disc (Ambush Beacon), which
        /// exempts it from every enemy-distance restriction. "Within" is inclusive, mirroring the
        /// corpus wording ("deployed within 6\" of this model").
        /// </summary>
        public static bool IsWaived<T>(PlaceObjectsRequest<T> request, Position candidate)
        {
            foreach (PlacementDisc waiver in request.EnemyDistanceWaiverDiscs)
            {
                if (Dist(candidate, waiver.Center) <= waiver.RadiusInches) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> breaks an enemy-distance restriction: closer than
        /// <see cref="PlaceObjectsRequest{T}.MinDistanceFromEnemiesInches"/> to any of
        /// <paramref name="enemyPositions"/> (the caller's own live, team-aware scan), or inside any
        /// keep-out disc — unless a waiver disc exempts it. Exclusive at the boundary ("over 9\" away"
        /// means exactly 9.0" is legal), matching the pre-existing per-resolver checks.
        /// </summary>
        public static bool ViolatesEnemyDistance<T>(PlaceObjectsRequest<T> request, Position candidate,
            IReadOnlyList<Position> enemyPositions)
        {
            float minDist = request.MinDistanceFromEnemiesInches;
            bool constrained = minDist > 0f || request.EnemyKeepOutDiscs.Count > 0;
            if (!constrained) return false;

            if (IsWaived(request, candidate)) return false;

            if (minDist > 0f)
            {
                foreach (Position enemy in enemyPositions)
                {
                    if (Dist(candidate, enemy) < minDist) return true;
                }
            }

            foreach (PlacementDisc keepOut in request.EnemyKeepOutDiscs)
            {
                if (Dist(candidate, keepOut.Center) < keepOut.RadiusInches) return true;
            }

            return false;
        }

        private static float Dist(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
