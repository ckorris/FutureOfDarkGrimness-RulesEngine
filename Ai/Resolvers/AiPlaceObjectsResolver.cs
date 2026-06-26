using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Auto-places a unit's models as one cohesion-valid block in the deployment zone. The models pack into
    /// a tight, square-ish grid (0.1" base-to-base, the same formation the movement resolvers use via
    /// <see cref="FDG.Stages.CohesiveFormation"/>), so the whole unit always satisfies BOTH cohesion rules —
    /// every model within 1" of a neighbour (<see cref="GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES"/>)
    /// and within 9" of every other model (<see cref="GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES"/>).
    /// A 10-wide single rank would break the 9" rule, so wrapping into ranks falls out of the grid for free.
    ///
    /// Successive units fan out across the zone (own lane, alternating Z bands) so the army spreads instead of
    /// stacking; that lane is only the *preferred* block centre — the resolver then searches outward for a
    /// centre where the intact block clears the zone shape, impassible terrain, already-placed models, and
    /// (for Ambush, when <see cref="PlaceObjectsRequest{T}.MinDistanceFromEnemiesInches"/> &gt; 0) every enemy
    /// model. If no fully-clear centre exists it places the block intact at the best clamped centre — cramped
    /// but never scattered, so a model is never stranded out of cohesion.
    /// </summary>
    public class AiPlaceObjectsResolver<T> : IStageResolver<PlaceObjectsRequest<T>, List<PlacedObjectEntry<T>>>
    {
        private readonly ITableState _tableState;
        private readonly Dictionary<PlayerID, int> _deployCountPerPlayer = new();

        // Fan-out spacing: successive units march across the zone in lanes this far apart, then wrap into Z
        // bands this far apart (alternating north/south of centre), so the AI spreads its army across the
        // deployment zone instead of stacking every unit in one spot.
        private const float FanOutLaneSpacingInches = 9f;
        private const float FanOutBandSpacingInches = 4.5f;

        //Snapshot of impassible terrain for the current Resolve call; models can't be placed overlapping it.
        private IReadOnlyList<ITerrain> _impassibleTerrain = System.Array.Empty<ITerrain>();

        public AiPlaceObjectsResolver(ITableState tableState)
        {
            _tableState = tableState;
        }

        public Task<List<PlacedObjectEntry<T>>> Resolve(PlaceObjectsRequest<T> request)
        {
            var zone = request.DeploymentZone;
            ZoneBounds bounds = zone.Bounds;
            _impassibleTerrain = _tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();
            float minEnemyDist = request.MinDistanceFromEnemiesInches;
            var enemies = minEnemyDist > 0f
                ? GetEnemyPositions(request.TargetPlayerID)
                : new List<Position>();

            _deployCountPerPlayer.TryGetValue(request.TargetPlayerID, out int deployIndex);
            _deployCountPerPlayer[request.TargetPlayerID] = deployIndex + 1;

            var models = request.ModelsToPlace;
            if (models.Count == 0)
                return Task.FromResult(new List<PlacedObjectEntry<T>>());

            float maxRadius = models.Select(b => GetBaseRadius(b.GetValue())).DefaultIfEmpty(0.75f).Max();
            float spacing = maxRadius * 2 + 0.1f; // 0.1" base-to-base, so adjacent models satisfy the 1" rule

            // Square-ish grid: minimises the block's diagonal, keeping it inside the 9" all-pairs rule for any
            // realistic unit size. Matches CohesiveFormation.PackGrid so deploy and movement form the same shape.
            int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(models.Count)));
            int rows = (int)MathF.Ceiling(models.Count / (float)cols);
            float gridWidth = (cols - 1) * spacing;
            float gridHeight = (rows - 1) * spacing;

            var existing = GetTableOccupants().ToList();

            // Preferred block centre: this unit's fan-out lane (across the zone width) + alternating Z band.
            float usableLeft = bounds.Left + maxRadius;
            float usableWidth = MathF.Max(0f, (bounds.Right - maxRadius) - usableLeft);
            float laneStep = MathF.Max(spacing, FanOutLaneSpacingInches);
            int lanes = Math.Max(1, (int)(usableWidth / laneStep));
            int lane = deployIndex % lanes;
            int band = deployIndex / lanes;
            int signedBand = (band % 2 == 0) ? band / 2 : -((band + 1) / 2); // 0, +1, -1, +2, -2, ...

            float preferredCx = usableLeft + lane * laneStep + gridWidth / 2f;
            float preferredCz = bounds.CenterZ + signedBand * FanOutBandSpacingInches;

            // Range of block centres that keep every cell inside the zone bounds. If the block is wider/taller
            // than the usable zone the range inverts; the clamps below then collapse it to the zone centre.
            float cxMin = usableLeft + gridWidth / 2f;
            float cxMax = (bounds.Right - maxRadius) - gridWidth / 2f;
            float czMin = (bounds.Bottom + maxRadius) + gridHeight / 2f;
            float czMax = (bounds.Top - maxRadius) - gridHeight / 2f;

            Position center = FindBlockCenter(zone, maxRadius, cols, spacing, gridWidth, gridHeight,
                models.Count, preferredCx, preferredCz, cxMin, cxMax, czMin, czMax, existing, enemies, minEnemyDist);

            var positions = BuildGrid(center, cols, spacing, gridWidth, gridHeight, models.Count);
            var placed = new List<PlacedObjectEntry<T>>(models.Count);
            for (int i = 0; i < models.Count; i++)
                placed.Add(new PlacedObjectEntry<T>(models[i], positions[i]));

            return Task.FromResult(placed);
        }

        // The model positions of a square-ish block centred at (center): cols per row, filling left→right,
        // top→bottom, for exactly `count` models (the last row may be partial).
        private static List<Position> BuildGrid(Position center, int cols, float spacing,
            float gridWidth, float gridHeight, int count)
        {
            var positions = new List<Position>(count);
            for (int k = 0; k < count; k++)
            {
                int col = k % cols, row = k / cols;
                positions.Add(new Position(
                    center.x - gridWidth / 2f + col * spacing,
                    center.z - gridHeight / 2f + row * spacing));
            }
            return positions;
        }

        // Searches block centres (preferred lane/band first, then spiralling outward in `spacing` steps within
        // the in-bounds range) for one where the whole block is legal. Falls back to the clamped preferred
        // centre — block intact, so the unit is cohesion-valid even when the zone is too cramped to be clear.
        private Position FindBlockCenter(IBoundedZone zone, float radius, int cols, float spacing,
            float gridWidth, float gridHeight, int count, float preferredCx, float preferredCz,
            float cxMin, float cxMax, float czMin, float czMax,
            List<(Position pos, float radius)> existing, List<Position> enemies, float minEnemyDist)
        {
            float startCx = Math.Clamp(preferredCx, MathF.Min(cxMin, cxMax), MathF.Max(cxMin, cxMax));
            float startCz = Math.Clamp(preferredCz, MathF.Min(czMin, czMax), MathF.Max(czMin, czMax));

            foreach (float cz in CandidateAxis(startCz, czMin, czMax, spacing))
                foreach (float cx in CandidateAxis(startCx, cxMin, cxMax, spacing))
                {
                    var center = new Position(cx, cz);
                    if (BlockIsValid(center, zone, radius, cols, spacing, gridWidth, gridHeight, count, existing, enemies, minEnemyDist))
                        return center;
                }

            return new Position(startCx, startCz);
        }

        // Values from `start` outward (start, +step, -step, +2step, …), clamped to [min,max]; if the range is
        // empty (block bigger than the zone) yields the midpoint once so the block still gets placed.
        private static IEnumerable<float> CandidateAxis(float start, float min, float max, float step)
        {
            if (min > max) { yield return (min + max) / 2f; yield break; }
            var seen = new HashSet<int>();
            for (int i = 0; ; i++)
            {
                float v = start + ((i % 2 == 0) ? 1 : -1) * ((i + 1) / 2) * step;
                if (v < min) v = min;
                else if (v > max) v = max;
                int key = (int)MathF.Round(v * 100f);
                if (seen.Add(key)) yield return v;
                // Stop once we've swept both ends.
                if (start - ((i + 1) / 2) * step <= min && start + ((i + 1) / 2) * step >= max) break;
            }
        }

        private bool BlockIsValid(Position center, IBoundedZone zone, float radius, int cols, float spacing,
            float gridWidth, float gridHeight, int count, List<(Position pos, float radius)> existing,
            List<Position> enemies, float minEnemyDist)
        {
            foreach (Position p in BuildGrid(center, cols, spacing, gridWidth, gridHeight, count))
            {
                if (!zone.IsPointWithinZone(p)) return false; // outside the true shape (e.g. a circle's corner)
                if (OverlapsExisting(p, radius, existing)) return false;
                if (PlacementUtilities.OverlapsImpassibleTerrain(p, radius, _impassibleTerrain)) return false;
                if (TooCloseToEnemy(p, enemies, minEnemyDist)) return false;
            }
            return true;
        }

        private List<Position> GetEnemyPositions(PlayerID self)
        {
            var positions = new List<Position>();
            foreach (var unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID == self) continue;
                foreach (var model in unit.Models)
                {
                    if (model is ModelData md && md.GetIsAlive() && (md.Position.x != 0f || md.Position.z != 0f))
                        positions.Add(md.Position);
                }
            }
            return positions;
        }

        private static bool TooCloseToEnemy(Position p, List<Position> enemies, float minDist)
        {
            foreach (var e in enemies)
                if (Dist(p, e) < minDist) return true;
            return false;
        }

        private IEnumerable<(Position, float)> GetTableOccupants()
        {
            foreach (var model in _tableState.Models.Objects)
            {
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;
                yield return (pos, model.BaseRadiusInches);
            }
        }

        private static bool OverlapsExisting(Position p, float r, List<(Position pos, float radius)> existing)
        {
            foreach (var (ep, er) in existing)
                if (Dist(p, ep) < r + er) return true;
            return false;
        }

        private static float Dist(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static float GetBaseRadius(T value) =>
            value is ModelData m ? m.BaseRadiusInches : 0.75f;
    }
}
