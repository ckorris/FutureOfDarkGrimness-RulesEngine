namespace FDG.Stages
{
    /// <summary>
    /// Shared objective-marker auto-placement. Picks a position that tends to balance the
    /// board along the short (Z) axis: the X coordinate is random (X imbalance favors neither
    /// team and a little asymmetry makes games more interesting), while Z mirrors the existing
    /// markers' centroid across the table center. A fine sampling of the legal band, sorted by
    /// distance to that target, is walked until the shared <see cref="ObjectivePlacementValidator"/>
    /// accepts a spot.
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
        private const float ZMirrorJitterInches = 2f;
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
            float targetX = RandomInRange(rng, band.Left, band.Right);

            // First placement: nothing to balance against. Random anywhere in the band.
            if (existing.Count == 0)
                return new Position(targetX, RandomInRange(rng, band.Bottom, band.Top));

            float tableCenterZ = (band.Bottom + band.Top) / 2f;
            float centroidZ = (float)existing.Average(o => (double)o.Position.z);
            float offsetZ = centroidZ - tableCenterZ;

            float targetZ;
            if (MathF.Abs(offsetZ) < NearCenterThresholdInches)
            {
                // Centroid is already at center — mirroring would just hit the same point.
                // Last placement: nudge near center to keep symmetry. Otherwise random,
                // since a future placement can still balance whatever we put here.
                bool isLastPlacement = markerIndex >= totalMarkers;
                targetZ = isLastPlacement
                    ? tableCenterZ + RandomInRange(rng, -NearCenterLastPlacementJitterInches, NearCenterLastPlacementJitterInches)
                    : RandomInRange(rng, band.Bottom, band.Top);
            }
            else
            {
                // Reflect Z through the table center, then jitter so play doesn't look mechanical.
                targetZ = tableCenterZ - offsetZ + RandomInRange(rng, -ZMirrorJitterInches, ZMirrorJitterInches);
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
