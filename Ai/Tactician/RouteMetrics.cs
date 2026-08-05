using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Walking distance around impassible terrain (#264 issue 1). Every gradient in the Tactician's
    /// score used to measure goals in a straight line, which pays essentially NOTHING for rounding a
    /// large wall - an 8" rush around a 20"-wide piece closes ~0.3" of a 25" straight-line gap, and a
    /// correct detour's first leg can even move AWAY from the marker. Retreat candidates then win the
    /// argmax on the flat reachable bonus alone and the unit walks backwards off the table.
    /// <para>
    /// The measure is one route per goal (not per candidate x goal): pathfind once from the unit's
    /// start, then price each candidate endpoint against that polyline as "how far off the route it
    /// sits, plus the route's own remainder from there". A fresh pathfind per candidate would blow the
    /// ~0.5s decision budget; this is a single A* per goal and a handful of segment projections.
    /// </para>
    /// </summary>
    public static class RouteMetrics
    {
        /// <summary>
        /// The polyline a unit of this base size walks from <paramref name="start"/> to
        /// <paramref name="goal"/>, detouring around impassible terrain - and, for a unit that does
        /// not ignore it, around difficult ground with a comparably-priced clear alternative (#281:
        /// the mover detours now, so a gradient that priced the straight lane would disagree with
        /// the path the unit actually walks and understate a muddy marker's real distance). The
        /// straight segment when the lane is clear - and also when NO route exists, so callers
        /// degrade to the old straight-line measure rather than to nonsense (#264 issue 3 fixes the
        /// null-route case at its source). The grid is a factory so a clear lane never pays for
        /// building one; pass <paramref name="ignoresDifficultTerrain"/> for a Strider unit so a
        /// mud-only lane skips the build too (its grid could never bend the route anyway).
        /// </summary>
        public static List<Position> Route(IReadOnlyList<ITerrain> terrain, Func<TerrainGrid> grid,
            Position start, Position goal, float baseRadiusInches, bool ignoresDifficultTerrain = false)
        {
            List<Position>? path = null;
            var startPoint = new Float2(start.x, start.z);
            var goalPoint = new Float2(goal.x, goal.z);
            bool blocked = terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Impassible)
                && t.Shape.DoesPathIntersectZone(startPoint, goalPoint, baseRadiusInches));
            bool throughDifficult = !ignoresDifficultTerrain
                && terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Difficult)
                    && t.Shape.DoesPathIntersectZone(startPoint, goalPoint, baseRadiusInches));
            if (blocked || throughDifficult)
                path = GridPathfinder.FindPath(grid(), terrain, start, goal, baseRadiusInches);
            return path ?? new List<Position> { start, goal };
        }

        /// <summary>Total arc length of a route.</summary>
        public static float Length(IReadOnlyList<Position> route)
        {
            float total = 0f;
            for (int i = 1; i < route.Count; i++) total += Distance(route[i - 1], route[i]);
            return total;
        }

        /// <summary>
        /// Remaining walking distance from an arbitrary point to the route's end: the shortest
        /// (offset onto the route + the route's own remainder from there) over every segment whose
        /// offset leg is actually WALKABLE. The clear-leg test is what makes this a distance rather
        /// than a wish - without it a point on the wrong side of a wall joins a later segment by
        /// teleporting through the wall, which handed a standing-still candidate a fat slice of the
        /// detour it had not walked. Equals <see cref="Length"/> when asked about the route's own
        /// start (its own first projection is itself, offset zero), so staying put scores exactly
        /// zero progress.
        /// </summary>
        public static float RemainingFrom(IReadOnlyList<Position> route, Position from,
            IReadOnlyList<ITerrain> terrain, float baseRadiusInches)
        {
            if (route.Count == 0) return 0f;
            if (route.Count == 1) return Distance(from, route[0]);

            // Backwards over the segments, carrying the arc from the segment's FAR node to the end.
            float best = float.PositiveInfinity;
            float bestIgnoringTerrain = float.PositiveInfinity;
            float remainingBeyond = 0f;
            for (int i = route.Count - 2; i >= 0; i--)
            {
                Position a = route[i], b = route[i + 1];
                float abx = b.x - a.x, abz = b.z - a.z;
                float lengthSq = abx * abx + abz * abz;
                float t = lengthSq <= 1e-8f
                    ? 0f
                    : Math.Clamp(((from.x - a.x) * abx + (from.z - a.z) * abz) / lengthSq, 0f, 1f);
                var projection = new Position(a.x + t * abx, a.z + t * abz);
                float segment = MathF.Sqrt(lengthSq);
                float total = Distance(from, projection) + (1f - t) * segment + remainingBeyond;
                remainingBeyond += segment;

                bestIgnoringTerrain = Math.Min(bestIgnoringTerrain, total);
                if (GridPathfinder.SegmentClear(terrain, from, projection, baseRadiusInches))
                    best = Math.Min(best, total);
            }
            // Walled off from every part of the route (a sealed pocket): the unconstrained measure
            // is still the least-wrong number available, and it never flatters a forward move.
            return float.IsPositiveInfinity(best) ? bestIgnoringTerrain : best;
        }

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
