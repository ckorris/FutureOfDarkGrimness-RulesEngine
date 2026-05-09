using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    public class AiSelectionResolver<T> : IStageResolver<SelectionRequest<T>, DataBinding<T>>
    {
        public Task<DataBinding<T>> Resolve(SelectionRequest<T> request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException($"AI received a {nameof(SelectionRequest<T>)} with no valid options.");

            return Task.FromResult(request.ValidOptions[0].Option);
        }
    }
}
