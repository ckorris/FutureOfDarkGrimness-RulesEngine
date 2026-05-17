using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Picks a legal position for an objective marker. Walks a shuffled grid
    /// of candidates inside the legal band and returns the first one the
    /// shared <see cref="ObjectivePlacementValidator"/> accepts.
    /// </summary>
    /// <remarks>
    /// Intentionally simple. The point of this resolver is to keep games
    /// moving when there isn't a human at a seat, not to play strategically —
    /// objective placement is a setup step, not a decision space where AI
    /// skill should matter. A smarter version (bias toward AI's own table
    /// half, prefer spread) can come later if needed.
    /// </remarks>
    public class AiPlaceObjectiveResolver : IStageResolver<PlaceObjectiveRequest, Position>
    {
        private const float CandidateGridStepInches = 3f;

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

            var candidates = new List<Position>();
            for (float x = band.Left; x <= band.Right; x += CandidateGridStepInches)
                for (float z = band.Bottom; z <= band.Top; z += CandidateGridStepInches)
                    candidates.Add(new Position(x, z));

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            foreach (var c in candidates)
            {
                var validity = ObjectivePlacementValidator.Check(
                    c, band, request.MinSeparationInches, existing, impassable);
                if (validity == ObjectivePlacementValidity.Valid)
                    return Task.FromResult(c);
            }

            // Pathological: no legal spot found. Fall back to the band centre so the
            // game can continue; PlaceOneObjectiveStage will validate again and re-prompt
            // (which would re-enter here and infinite-loop) — but a no-legal-spot table
            // can't be played anyway, so surface it loudly.
            throw new InvalidOperationException(
                "AI could not find a legal objective placement. Terrain/marker layout has no valid spots in the legal band.");
        }
    }
}
