using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Objective placement favoring the army's own profile (#191 A4b-2). Deployment zones are
    /// chosen AFTER objectives, so "own side" is unknowable here - the side-agnostic lever is
    /// cluster-vs-spread along X: a shooting army wants the markers close together (one firebase
    /// covers them all), a fast or melee army wants them wide (mobility wins the races). Z keeps
    /// the solo bot's balancing idea (reflect the existing centroid through the band centre) but
    /// deterministic - no jitter, no RNG. Legality via the stage's own ObjectivePlacementValidator,
    /// walking a 1" grid sorted by distance to the target exactly like solo.
    /// </summary>
    public class TacticianPlaceObjectiveResolver : IStageResolver<PlaceObjectiveRequest, Position>
    {
        private const float CandidateStepInches = 1f;
        // Wide placement: this fraction of the band's half-width off centre, alternating sides.
        private const float SpreadFraction = 0.7f;
        // An army whose models mostly carry >= this range prefers clustered markers.
        private const float ShootyRangeInches = 18f;

        private readonly ITableState _tableState;

        public TacticianPlaceObjectiveResolver(ITableState tableState)
        {
            _tableState = tableState;
        }

        public Task<Position> Resolve(PlaceObjectiveRequest request)
        {
            RectangularZone band = request.LegalBand;
            List<IObjective> existing = _tableState.Objectives.Objects.ToList();
            var impassable = _tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();

            Position target = ChooseTarget(request, band, existing);

            var candidates = new List<Position>();
            for (float x = band.Left; x <= band.Right; x += CandidateStepInches)
                for (float z = band.Bottom; z <= band.Top; z += CandidateStepInches)
                    candidates.Add(new Position(x, z));
            candidates.Sort((a, b) => DistSq(a, target).CompareTo(DistSq(b, target)));

            foreach (Position c in candidates)
            {
                if (ObjectivePlacementValidator.Check(c, band, request.MinSeparationInches,
                        existing, impassable) == ObjectivePlacementValidity.Valid)
                    return Task.FromResult(c);
            }

            throw new InvalidOperationException(
                "Tactician could not find a legal objective placement. Terrain/marker layout has no valid spots in the legal band.");
        }

        private Position ChooseTarget(PlaceObjectiveRequest request, RectangularZone band,
            List<IObjective> existing)
        {
            float centerX = (band.Left + band.Right) / 2f;
            float centerZ = (band.Bottom + band.Top) / 2f;
            float halfWidth = (band.Right - band.Left) / 2f;

            // Cluster: successive markers step MinSeparation apart around centre (0, +sep, -sep,
            // +2sep, ...) so they stay legal but tight. Spread: alternate wide across the band.
            int k = request.MarkerIndex; // 1-based
            float offset;
            if (ArmyPrefersCluster(request.TargetPlayerID))
            {
                int step = k / 2;
                float sep = Math.Max(request.MinSeparationInches, 1f);
                offset = (k % 2 == 0 ? 1f : -1f) * step * sep;
            }
            else
            {
                offset = (k % 2 == 0 ? 1f : -1f) * halfWidth * SpreadFraction;
                if (k == 1) offset = 0f; // first marker central; later ones race wide
            }
            float targetX = Math.Clamp(centerX + offset, band.Left, band.Right);

            // Z balance, solo's idea made deterministic: reflect the existing centroid through the
            // band centre so neither future side of the table is favored.
            float targetZ = centerZ;
            if (existing.Count > 0)
            {
                float centroidZ = (float)existing.Average(o => (double)o.Position.z);
                targetZ = Math.Clamp(2f * centerZ - centroidZ, band.Bottom, band.Top);
            }
            return new Position(targetX, targetZ);
        }

        // Model-count majority: an army mostly carrying long-range guns fights as a firebase and
        // wants tight markers; everything else (melee, short-range, fast mixed) wants them wide.
        private bool ArmyPrefersCluster(PlayerID player)
        {
            int longRange = 0, total = 0;
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (army.PlayerID != player || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                    foreach (IModel model in unit.GetValue().Models)
                    {
                        total++;
                        if (model.Weapons.Any(w => w.RangeInches >= ShootyRangeInches)) longRange++;
                    }
            }
            return total > 0 && longRange * 2 >= total;
        }

        private static float DistSq(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
