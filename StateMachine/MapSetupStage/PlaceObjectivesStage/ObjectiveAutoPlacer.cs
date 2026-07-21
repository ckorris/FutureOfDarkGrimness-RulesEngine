namespace FDG.Stages
{
    /// <summary>
    /// Shared objective-marker auto-placement. Picks a position that tends to balance the board on
    /// BOTH axes: each new marker's target mirrors the existing markers' centroid across the table
    /// center (with jitter), on the long (X) axis as well as the short (Z) axis, so markers spread
    /// out instead of clustering. A fine sampling of the legal band, sorted by distance to that
    /// target, is walked until the shared <see cref="ObjectivePlacementValidator"/> accepts a spot.
    /// </summary>
    /// <remarks>
    /// Both the engine's Auto-Placed map-setup path (<see cref="PlaceOneObjectiveStage"/>) and the
    /// solo-rules AI (<see cref="FDG.Ai.Resolvers.AiPlaceObjectiveResolver"/>) call this, so the two
    /// produce identical placements for the same table state and random stream. The <paramref name="rng"/>
    /// draw order is load-bearing: a seeded run reproduces its layout exactly (#193), so keep the order
    /// of <see cref="RandomInRange"/> calls stable.
    /// </remarks>
    public static class ObjectiveAutoPlacer
    {
        // 1" sampling is fine enough to read as continuous; the validator is the gatekeeper.
        private const float CandidateStepInches = 1f;
        private const float MirrorJitterInches = 2f;
        private const float NearCenterThresholdInches = 1f;
        private const float NearCenterLastPlacementJitterInches = 0.5f;

        /// <summary>
        /// Chooses a legal placement for the next marker, or returns false if the legal band has no
        /// valid spot left (caller decides whether to skip or throw).
        /// </summary>
        /// <param name="markerIndex">1-based index of the marker being placed.</param>
        /// <param name="totalMarkers">Total markers this game (used only for the last-placement symmetry case).</param>
        public static bool TryChoosePlacement(
            RectangularZone band,
            float minSeparationInches,
            int markerIndex,
            int totalMarkers,
            IReadOnlyList<IObjective> existing,
            IReadOnlyList<ITerrain> impassable,
            Random rng,
            out Position placement)
        {
            Position target = ChooseTarget(band, markerIndex, totalMarkers, existing, rng);

            var candidates = BuildCandidates(band);
            candidates.Sort((a, b) => DistSq(a, target).CompareTo(DistSq(b, target)));

            foreach (var c in candidates)
            {
                if (ObjectivePlacementValidator.Check(c, band, minSeparationInches, existing, impassable)
                    == ObjectivePlacementValidity.Valid)
                {
                    placement = c;
                    return true;
                }
            }

            placement = default;
            return false;
        }

        private static Position ChooseTarget(RectangularZone band, int markerIndex, int totalMarkers,
            IReadOnlyList<IObjective> existing, Random rng)
        {
            // First placement: nothing to balance against. Random anywhere in the band.
            if (existing.Count == 0)
                return new Position(RandomInRange(rng, band.Left, band.Right),
                                    RandomInRange(rng, band.Bottom, band.Top));

            // Balance both axes so markers spread rather than cluster: each new target mirrors the
            // existing centroid across the table center. X first, then Z, to keep the rng draw order
            // stable for seeded replays (#193).
            bool isLastPlacement = markerIndex >= totalMarkers;
            float centroidX = (float)existing.Average(o => (double)o.Position.x);
            float centroidZ = (float)existing.Average(o => (double)o.Position.z);
            float targetX = MirrorAcrossCenter(band.Left, band.Right, centroidX, isLastPlacement, rng);
            float targetZ = MirrorAcrossCenter(band.Bottom, band.Top, centroidZ, isLastPlacement, rng);
            return new Position(targetX, targetZ);
        }

        // Reflects the existing centroid through the axis center to pull the next marker to the opposite
        // side, with jitter so play doesn't look mechanical. When the centroid already sits at center
        // (mirroring would hit the same point) the last marker nudges near center to keep symmetry, while
        // earlier markers go random since a future placement can still balance whatever we put here.
        private static float MirrorAcrossCenter(float min, float max, float centroid,
            bool isLastPlacement, Random rng)
        {
            float center = (min + max) / 2f;
            float offset = centroid - center;

            float target;
            if (MathF.Abs(offset) < NearCenterThresholdInches)
                target = isLastPlacement
                    ? center + RandomInRange(rng, -NearCenterLastPlacementJitterInches, NearCenterLastPlacementJitterInches)
                    : RandomInRange(rng, min, max);
            else
                target = center - offset + RandomInRange(rng, -MirrorJitterInches, MirrorJitterInches);

            // The validator will reject anything outside the band, but clamping keeps the
            // sort-by-distance ordering sensible when the reflected target lands past an edge.
            return MathF.Max(min, MathF.Min(max, target));
        }

        private static List<Position> BuildCandidates(RectangularZone band)
        {
            var list = new List<Position>();
            for (float x = band.Left; x <= band.Right; x += CandidateStepInches)
                for (float z = band.Bottom; z <= band.Top; z += CandidateStepInches)
                    list.Add(new Position(x, z));
            return list;
        }

        private static float RandomInRange(Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);

        private static float DistSq(Position a, Position b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
