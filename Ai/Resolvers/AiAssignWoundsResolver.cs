using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    public class AiAssignWoundsResolver : IStageResolver<AssignWoundsRequest, AssignWoundsResults>
    {
        public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
        {
            var results = new AssignWoundsResults(request.UnitReceivingWounds, request.TotalWoundsToAssign);
            results.AutoFill();
            return Task.FromResult(results);
        }
    }
}
