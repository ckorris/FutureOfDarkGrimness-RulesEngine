using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Picks a melee defender unit with the fewest remaining alive models.
    /// Replies with Selected — the AI never backs out.
    /// </summary>
    public class AiChooseMeleeDefenderResolver
        : IStageResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>
    {
        public Task<CancellableResult<DataBinding<UnitData>>> Resolve(CancellableSelectionRequest<UnitData> request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException("AI received a melee defender request with no valid options.");

            var weakest = request.ValidOptions
                .OrderBy(o => o.Option.GetValue().Models.Count(m => ((ModelData)m).GetIsAlive()))
                .First();

            CancellableResult<DataBinding<UnitData>> result = new Selected<DataBinding<UnitData>>(weakest.Option);
            return Task.FromResult(result);
        }
    }
}
