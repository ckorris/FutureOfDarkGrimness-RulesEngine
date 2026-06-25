using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    public class AiYesNoResolver : IStageResolver<YesNoRequest, bool>
    {
        // The AI answers each yes/no with the default the request declares (YesNoRequest.DefaultAnswer), so
        // the choice is explicit per question instead of a blanket "always yes" that would silently accept a
        // future question whose correct answer is "no".
        public Task<bool> Resolve(YesNoRequest request) => Task.FromResult(request.DefaultAnswer);
    }
}
