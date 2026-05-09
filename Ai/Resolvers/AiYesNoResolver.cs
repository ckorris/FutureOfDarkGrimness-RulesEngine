using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    public class AiYesNoResolver : IStageResolver<YesNoRequest, bool>
    {
        public Task<bool> Resolve(YesNoRequest request) => Task.FromResult(true);
    }
}
