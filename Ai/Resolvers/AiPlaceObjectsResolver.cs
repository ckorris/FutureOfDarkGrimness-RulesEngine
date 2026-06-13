using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Auto-places models in the deployment zone in a row near the zone's center Z,
    /// scanning left to right with per-call row staggering to avoid overlap.
    ///
    /// When the request sets <see cref="PlaceObjectsRequest{T}.MinDistanceFromEnemiesInches"/> &gt; 0
    /// (Ambush reserve arrival), it instead scans Z rows across the whole zone to find one where the
    /// unit fits clear of overlaps AND over that distance from every enemy model.
    /// </summary>
    public class AiPlaceObjectsResolver<T> : IStageResolver<PlaceObjectsRequest<T>, List<PlacedObjectEntry<T>>>
    {
        private readonly ITableState _tableState;
        private readonly Dictionary<PlayerID, int> _deployCountPerPlayer = new();
        private const float ZRowOffset = 2f;

        //Snapshot of impassible terrain for the current Resolve call; models can't be placed overlapping it.
        private IReadOnlyList<ITerrain> _impassibleTerrain = System.Array.Empty<ITerrain>();

        public AiPlaceObjectsResolver(ITableState tableState)
        {
            _tableState = tableState;
        }

        public Task<List<PlacedObjectEntry<T>>> Resolve(PlaceObjectsRequest<T> request)
        {
            var zone = request.DeploymentZone.GetValue();
            _impassibleTerrain = _tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();
            float minEnemyDist = request.MinDistanceFromEnemiesInches;
            var enemies = minEnemyDist > 0f
                ? GetEnemyPositions(request.TargetPlayerID)
                : new List<Position>();

            _deployCountPerPlayer.TryGetValue(request.TargetPlayerID, out int deployIndex);
            _deployCountPerPlayer[request.TargetPlayerID] = deployIndex + 1;

            float maxRadius = request.ModelsToPlace
                .Select(b => GetBaseRadius(b.GetValue()))
                .DefaultIfEmpty(0.75f)
                .Max();
            float spacing = maxRadius * 2 + 0.1f;
            float xStagger = (deployIndex % 2) * spacing / 2f;

            var existing = GetTableOccupants().ToList();

            // Enemy-constrained (Ambush): scan rows across the zone for one where the whole unit fits.
            if (minEnemyDist > 0f)
            {
                for (float cz = zone.Bottom + maxRadius; cz <= zone.Top - maxRadius; cz += spacing)
                {
                    var rowPlacement = TryPlaceRow(request, zone, cz, spacing, xStagger, existing, enemies, minEnemyDist);
                    if (rowPlacement != null)
                        return Task.FromResult(rowPlacement);
                }
                // No legal row found — fall through to best-effort below (rare on a real table).
            }

            float zoneCz = (zone.Bottom + zone.Top) / 2f;
            float defaultCz = Math.Clamp(zoneCz + deployIndex * ZRowOffset, zone.Bottom + maxRadius, zone.Top - maxRadius);

            var placed = new List<PlacedObjectEntry<T>>();
            foreach (var binding in request.ModelsToPlace)
            {
                float r = GetBaseRadius(binding.GetValue());
                var pos = FindPosition(r, spacing, zone, defaultCz, xStagger, placed, existing, enemies, minEnemyDist);
                placed.Add(new PlacedObjectEntry<T>(binding, pos));
            }

            return Task.FromResult(placed);
        }

        // Tries to place every model in a single row at Z=cz; returns null if any model can't fit legally.
        private List<PlacedObjectEntry<T>>? TryPlaceRow(PlaceObjectsRequest<T> request, RectangularZone zone,
            float cz, float spacing, float xStagger, List<(Position pos, float radius)> existing,
            List<Position> enemies, float minEnemyDist)
        {
            var placed = new List<PlacedObjectEntry<T>>();
            foreach (var binding in request.ModelsToPlace)
            {
                float r = GetBaseRadius(binding.GetValue());
                Position? pos = FindValidPosition(r, spacing, zone, cz, xStagger, placed, existing, enemies, minEnemyDist);
                if (pos == null) return null;
                placed.Add(new PlacedObjectEntry<T>(binding, pos.Value));
            }
            return placed;
        }

        // Strict search: returns null rather than a fallback when nothing in the row is legal.
        private Position? FindValidPosition(float r, float step, RectangularZone zone, float cz,
            float xStagger, List<PlacedObjectEntry<T>> placedSoFar, List<(Position pos, float radius)> existing,
            List<Position> enemies, float minEnemyDist)
        {
            for (float x = zone.Left + r + xStagger; x <= zone.Right - r; x += step)
            {
                var candidate = new Position(x, cz);
                if (OverlapsAny(candidate, r, placedSoFar)) continue;
                if (OverlapsExisting(candidate, r, existing)) continue;
                if (PlacementUtilities.OverlapsImpassibleTerrain(candidate, r, _impassibleTerrain)) continue;
                if (TooCloseToEnemy(candidate, enemies, minEnemyDist)) continue;
                if (placedSoFar.Count > 0 && !InCohesion(candidate, r, placedSoFar)) continue;
                return candidate;
            }
            return null;
        }

        private Position FindPosition(float r, float step, RectangularZone zone, float cz,
            float xStagger, List<PlacedObjectEntry<T>> placedSoFar,
            List<(Position pos, float radius)> existing, List<Position> enemies, float minEnemyDist)
        {
            Position? valid = FindValidPosition(r, step, zone, cz, xStagger, placedSoFar, existing, enemies, minEnemyDist);
            if (valid != null) return valid.Value;
            return new Position(Math.Clamp((zone.Left + zone.Right) / 2f, zone.Left + r, zone.Right - r), cz);
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

        private static bool OverlapsAny(Position p, float r, List<PlacedObjectEntry<T>> placed)
        {
            foreach (var e in placed)
                if (Dist(p, e.Position) < r + GetBaseRadius(e.Binding.GetValue())) return true;
            return false;
        }

        private static bool OverlapsExisting(Position p, float r, List<(Position pos, float radius)> existing)
        {
            foreach (var (ep, er) in existing)
                if (Dist(p, ep) < r + er) return true;
            return false;
        }

        private static bool InCohesion(Position p, float r, List<PlacedObjectEntry<T>> placed)
        {
            foreach (var e in placed)
            {
                float er = GetBaseRadius(e.Binding.GetValue());
                if (Dist(p, e.Position) - r - er <= GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES)
                    return true;
            }
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
