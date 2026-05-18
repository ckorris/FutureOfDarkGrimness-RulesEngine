using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Picks a random template from the pool and a random legal position via
    /// rejection sampling. Deliberately dumb — the point is plausible play, not
    /// balanced or interesting layouts. Per #002 Decisions: smart terrain AI is
    /// out of scope for the initial implementation.
    /// </summary>
    public class AiPlaceOneTerrainResolver : IStageResolver<PlaceOneTerrainRequest, TerrainPlacementResult>
    {
        private const int MaxAttempts = 200;

        private readonly ITableState _tableState;
        private readonly Random _rng = new();

        public AiPlaceOneTerrainResolver(ITableState tableState)
        {
            _tableState = tableState;
        }

        public Task<TerrainPlacementResult> Resolve(PlaceOneTerrainRequest request)
        {
            if (request.Pool.Count == 0)
                throw new InvalidOperationException($"{nameof(AiPlaceOneTerrainResolver)} received empty pool.");

            var existing = _tableState.Terrain.Objects.ToList();

            // Try repeatedly: random template, random center, accept if validator passes.
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int templateIndex = _rng.Next(request.Pool.Count);
                TerrainPieceEntry template = request.Pool[templateIndex];

                Float2 center = RandomInteriorPoint(template.Shape, request.TableWidthInches, request.TableHeightInches);
                IZone candidate = TerrainTemplateUtilities.TranslateToCenter(template.Shape, center);

                var validity = TerrainPlacementValidator.Check(
                    candidate, request.TableWidthInches, request.TableHeightInches, existing);

                if (validity == TerrainPlacementValidity.Valid)
                    return Task.FromResult(new TerrainPlacementResult(templateIndex, center));
            }

            // Fell through — likely a very crowded table. Find the smallest template
            // and brute-force a grid search; if even that fails, the engine's
            // re-prompt loop will catch the resulting invalid result.
            return Task.FromResult(GridSearchFallback(request, existing));
        }

        /// <summary>Random table-interior point that keeps the template's footprint inside the table.</summary>
        private Float2 RandomInteriorPoint(IZone template, float tableW, float tableH)
        {
            (float halfW, float halfH) = GetHalfExtents(template);
            float minX = halfW;
            float maxX = tableW - halfW;
            float minY = halfH;
            float maxY = tableH - halfH;

            // Degenerate (template too big for table): just return the table center;
            // the validator will reject it and we'll fall through to GridSearchFallback.
            if (minX >= maxX || minY >= maxY)
                return new Float2(tableW * 0.5f, tableH * 0.5f);

            return new Float2(
                minX + (float)_rng.NextDouble() * (maxX - minX),
                minY + (float)_rng.NextDouble() * (maxY - minY));
        }

        private static (float halfW, float halfH) GetHalfExtents(IZone zone) => zone switch
        {
            RectangularZone r => ((r.Right - r.Left) * 0.5f, (r.Top - r.Bottom) * 0.5f),
            CircularZone c => (c.Radius, c.Radius),
            _ => throw new NotSupportedException($"Unsupported zone type: {zone.GetType().Name}.")
        };

        private TerrainPlacementResult GridSearchFallback(PlaceOneTerrainRequest request, List<ITerrain> existing)
        {
            const float StepInches = 2f;

            // Smallest template first — most likely to fit somewhere.
            var templatesBySize = Enumerable.Range(0, request.Pool.Count)
                .OrderBy(i => FootprintArea(request.Pool[i].Shape))
                .ToList();

            foreach (int idx in templatesBySize)
            {
                IZone template = request.Pool[idx].Shape;
                (float halfW, float halfH) = GetHalfExtents(template);

                for (float x = halfW; x <= request.TableWidthInches - halfW; x += StepInches)
                {
                    for (float y = halfH; y <= request.TableHeightInches - halfH; y += StepInches)
                    {
                        var center = new Float2(x, y);
                        var candidate = TerrainTemplateUtilities.TranslateToCenter(template, center);
                        var validity = TerrainPlacementValidator.Check(
                            candidate, request.TableWidthInches, request.TableHeightInches, existing);
                        if (validity == TerrainPlacementValidity.Valid)
                            return new TerrainPlacementResult(idx, center);
                    }
                }
            }

            // Nothing fits — return template 0 at table center; the engine's re-prompt
            // loop will reject it and re-emit, which will land here again. Acceptable
            // failure mode for the v1 dumb AI.
            return new TerrainPlacementResult(
                0, new Float2(request.TableWidthInches * 0.5f, request.TableHeightInches * 0.5f));
        }

        private static float FootprintArea(IZone zone) => zone switch
        {
            RectangularZone r => (r.Right - r.Left) * (r.Top - r.Bottom),
            CircularZone c => MathF.PI * c.Radius * c.Radius,
            _ => 0f,
        };
    }
}
