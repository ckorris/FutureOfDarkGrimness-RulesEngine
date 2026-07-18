using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Places an objective marker via the shared <see cref="ObjectiveAutoPlacer"/>: random X, Z
    /// mirrored across the table center to balance the board, first validator-legal spot nearest
    /// that target. The engine's Auto-Placed map-setup path uses the same helper, so a solo-rules
    /// game places markers identically whether or not the lobby chose interactive placement.
    /// </summary>
    public class AiPlaceObjectiveResolver : IStageResolver<PlaceObjectiveRequest, Position>
    {
        private readonly ITableState _tableState;
        private readonly Random _rng;

        /// <param name="rng">
        /// The player's seeded random stream (#193). Null keeps the historical unseeded behavior; the
        /// distribution of placements is unchanged either way, only its reproducibility.
        /// </param>
        public AiPlaceObjectiveResolver(ITableState tableState, Random? rng = null)
        {
            _tableState = tableState;
            _rng = rng ?? new Random();
        }

        public Task<Position> Resolve(PlaceObjectiveRequest request)
        {
            var existing = _tableState.Objectives.Objects.ToList();
            var impassable = _tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();

            if (ObjectiveAutoPlacer.TryChoosePlacement(
                    request.LegalBand, request.MinSeparationInches,
                    request.MarkerIndex, request.TotalMarkers,
                    existing, impassable, _rng, out Position placement))
                return Task.FromResult(placement);

            throw new InvalidOperationException(
                "AI could not find a legal objective placement. Terrain/marker layout has no valid spots in the legal band.");
        }
    }
}
