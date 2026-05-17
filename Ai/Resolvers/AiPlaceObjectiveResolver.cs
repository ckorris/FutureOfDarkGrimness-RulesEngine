using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Picks a position for an objective marker that tends to balance the board
    /// along the short (Z) axis. The X coordinate is random — X imbalance doesn't
    /// favor either team and a little asymmetry there makes games more interesting.
    /// </summary>
    /// <remarks>
    /// Algorithm:
    ///   First placement: random X, random Z in the legal band.
    ///   Otherwise: random X, target Z = reflect(existing centroid Z) through table
    ///   center, plus a small jitter. Then walk a fine sampling of the band sorted
    ///   by distance to (targetX, targetZ) and return the first position the shared
    ///   <see cref="ObjectivePlacementValidator"/> accepts.
    ///
    /// Special case: if the existing centroid is essentially already at the center
    /// (so mirroring it would produce a near-duplicate of the original target), the
    /// last placement stays near center to preserve symmetry; otherwise it falls
    /// back to a random target.
    /// </remarks>
    public class AiPlaceObjectiveResolver : IStageResolver<PlaceObjectiveRequest, Position>
    {
        // 1" sampling is fine enough to read as continuous; the validator is the gatekeeper.
        private const float CandidateStepInches = 1f;
        private const float ZMirrorJitterInches = 2f;
        private const float NearCenterThresholdInches = 1f;
        private const float NearCenterLastPlacementJitterInches = 0.5f;

        private readonly ITableState _tableState;
        private readonly Random _rng = new();

        public AiPlaceObjectiveResolver(ITableState tableState)
        {
            _tableState = tableState;
        }

        public Task<Position> Resolve(PlaceObjectiveRequest request)
        {
            var band = request.LegalBand;
            var existing = _tableState.Objectives.Objects.ToList();
            var impassable = _tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();

            Position target = ChooseTarget(request, band, existing);

            var candidates = BuildCandidates(band);
            candidates.Sort((a, b) =>
                DistSq(a, target).CompareTo(DistSq(b, target)));

            foreach (var c in candidates)
            {
                var validity = ObjectivePlacementValidator.Check(
                    c, band, request.MinSeparationInches, existing, impassable);
                if (validity == ObjectivePlacementValidity.Valid)
                    return Task.FromResult(c);
            }

            throw new InvalidOperationException(
                "AI could not find a legal objective placement. Terrain/marker layout has no valid spots in the legal band.");
        }

        private Position ChooseTarget(PlaceObjectiveRequest request, RectangularZone band, List<IObjective> existing)
        {
            float targetX = RandomInRange(band.Left, band.Right);

            // First placement: nothing to balance against. Random anywhere in the band.
            if (existing.Count == 0)
                return new Position(targetX, RandomInRange(band.Bottom, band.Top));

            float tableCenterZ = (band.Bottom + band.Top) / 2f;
            float centroidZ = (float)existing.Average(o => (double)o.Position.z);
            float offsetZ = centroidZ - tableCenterZ;

            float targetZ;
            if (MathF.Abs(offsetZ) < NearCenterThresholdInches)
            {
                // Centroid is already at center — mirroring would just hit the same point.
                // Last placement: nudge near center to keep symmetry. Otherwise random,
                // since a future placement can still balance whatever we put here.
                bool isLastPlacement = request.MarkerIndex >= request.TotalMarkers;
                targetZ = isLastPlacement
                    ? tableCenterZ + RandomInRange(-NearCenterLastPlacementJitterInches, NearCenterLastPlacementJitterInches)
                    : RandomInRange(band.Bottom, band.Top);
            }
            else
            {
                // Reflect Z through the table center, then jitter so play doesn't look mechanical.
                targetZ = tableCenterZ - offsetZ + RandomInRange(-ZMirrorJitterInches, ZMirrorJitterInches);
            }

            // The validator will reject anything outside the band, but clamping keeps the
            // sort-by-distance ordering sensible when the reflected target lands past an edge.
            targetZ = MathF.Max(band.Bottom, MathF.Min(band.Top, targetZ));
            return new Position(targetX, targetZ);
        }

        private static List<Position> BuildCandidates(RectangularZone band)
        {
            var list = new List<Position>();
            for (float x = band.Left; x <= band.Right; x += CandidateStepInches)
                for (float z = band.Bottom; z <= band.Top; z += CandidateStepInches)
                    list.Add(new Position(x, z));
            return list;
        }

        private float RandomInRange(float min, float max) =>
            min + (float)_rng.NextDouble() * (max - min);

        private static float DistSq(Position a, Position b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
