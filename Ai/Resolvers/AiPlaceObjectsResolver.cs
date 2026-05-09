using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Auto-places models in the deployment zone in a row near the zone's center Z,
    /// scanning left to right with per-call row staggering to avoid overlap.
    /// </summary>
    public class AiPlaceObjectsResolver<T> : IStageResolver<PlaceObjectsRequest<T>, List<PlacedObjectEntry<T>>>
    {
        private readonly ITableState _tableState;
        private readonly Dictionary<PlayerID, int> _deployCountPerPlayer = new();
        private const float ZRowOffset = 2f;

        public AiPlaceObjectsResolver(ITableState tableState)
        {
            _tableState = tableState;
        }

        public Task<List<PlacedObjectEntry<T>>> Resolve(PlaceObjectsRequest<T> request)
        {
            var zone = request.DeploymentZone.GetValue();

            _deployCountPerPlayer.TryGetValue(request.TargetPlayerID, out int deployIndex);
            _deployCountPerPlayer[request.TargetPlayerID] = deployIndex + 1;

            float maxRadius = request.ModelsToPlace
                .Select(b => GetBaseRadius(b.GetValue()))
                .DefaultIfEmpty(0.75f)
                .Max();
            float spacing = maxRadius * 2 + 0.1f;

            float zoneCz = (zone.Bottom + zone.Top) / 2f;
            float cz = Math.Clamp(zoneCz + deployIndex * ZRowOffset, zone.Bottom + maxRadius, zone.Top - maxRadius);
            float xStagger = (deployIndex % 2) * spacing / 2f;

            var existing = GetTableOccupants().ToList();
            var placed = new List<PlacedObjectEntry<T>>();

            foreach (var binding in request.ModelsToPlace)
            {
                float r = GetBaseRadius(binding.GetValue());
                var pos = FindPosition(r, spacing, zone, cz, xStagger, placed, existing);
                placed.Add(new PlacedObjectEntry<T>(binding, pos));
            }

            return Task.FromResult(placed);
        }

        private Position FindPosition(float r, float step, RectangularZone zone, float cz,
            float xStagger, List<PlacedObjectEntry<T>> placedSoFar,
            List<(Position pos, float radius)> existing)
        {
            for (float x = zone.Left + r + xStagger; x <= zone.Right - r; x += step)
            {
                var candidate = new Position(x, cz);
                if (OverlapsAny(candidate, r, placedSoFar)) continue;
                if (OverlapsExisting(candidate, r, existing)) continue;
                if (placedSoFar.Count > 0 && !InCohesion(candidate, r, placedSoFar)) continue;
                return candidate;
            }
            return new Position(Math.Clamp((zone.Left + zone.Right) / 2f, zone.Left + r, zone.Right - r), cz);
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
