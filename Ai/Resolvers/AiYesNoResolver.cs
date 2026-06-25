using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    public class AiYesNoResolver : IStageResolver<YesNoRequest, bool>
    {
        // The AI answers each yes/no with the default the request declares for it (YesNoRequest.AiPrefersYes),
        // so the AI's choice is explicit per question instead of a blanket "always yes" that would silently
        // accept a future question whose correct AI answer is "no".
        public Task<bool> Resolve(YesNoRequest request) => Task.FromResult(request.AiPrefersYes);
    }
}
