using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    public class AiChooseDeploymentZoneResolver : IStageResolver<ChooseDeploymentZoneRequest, DataBinding<RectangularZone>>
    {
        public Task<DataBinding<RectangularZone>> Resolve(ChooseDeploymentZoneRequest request)
        {
            if (request.AvailableZones.Count == 0)
                throw new InvalidOperationException("AI received a deployment zone request with no available zones.");

            return Task.FromResult(request.AvailableZones[0]);
        }
    }
}
