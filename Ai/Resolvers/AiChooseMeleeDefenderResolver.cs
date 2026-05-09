using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Picks the melee defender with the fewest remaining alive models (i.e. the weakest unit).
    /// Falls back to first valid option.
    /// </summary>
    public class AiChooseMeleeDefenderResolver : IStageResolver<ChooseMeleeDefenderRequest, DataBinding<UnitData>>
    {
        public Task<DataBinding<UnitData>> Resolve(ChooseMeleeDefenderRequest request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException("AI received a melee defender request with no valid options.");

            var weakest = request.ValidOptions
                .OrderBy(o => o.Option.GetValue().Models.Count(m => ((ModelData)m).GetIsAlive()))
                .First();

            return Task.FromResult(weakest.Option);
        }
    }
}
