using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #029 — answers the Aircraft forced-move request: the shortest distance in the 30–36" band that keeps
    /// every base on the table (staying in play beats flying far). When no distance in the band stays on,
    /// the fly-off is mandatory, so it confirms it at the minimum distance.
    /// </summary>
    public class AiAircraftAdvanceResolver : IStageResolver<AircraftAdvanceRequest, AircraftAdvanceResult>
    {
        public Task<AircraftAdvanceResult> Resolve(AircraftAdvanceRequest request)
        {
            for (float d = request.MinDistanceInches; d <= request.MaxDistanceInches + 0.001f; d += 1f)
            {
                var paths = ForcedAircraftMove.BuildPaths(request.UnitDataBinding, request.HeadingNormal, d);
                if (!ForcedAircraftMove.WouldLeaveTable(paths))
                    return Task.FromResult(new AircraftAdvanceResult(d, false));
            }

            return Task.FromResult(new AircraftAdvanceResult(request.MinDistanceInches, true));
        }
    }
}
