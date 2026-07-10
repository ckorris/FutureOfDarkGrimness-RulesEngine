using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Single-model target picks (#191 A5-6, Chris's resolver pass): Takedown/sniper shots and
    /// single-model spells previously took solo's first-option - "Model 1" - when WHICH model dies
    /// is the whole point of the rule. Pick the model whose removal hurts most: weapon output
    /// (attacks x AP weight, the A4-4 currency) plus a bonus for models carrying their own rules -
    /// a joined hero's rules live on its MODEL after the #006 merge, so that is the hero-sniping
    /// signal. Every other model selection falls through to solo.
    /// </summary>
    public class TacticianModelSelectionResolver
        : IStageResolver<SelectionRequest<ModelData>, DataBinding<ModelData>>
    {
        // Both senders end their instructions with this: "Takedown: choose the target model" and
        // "{spellName}: choose the target model".
        public const string ModelPickInstructionSuffix = ": choose the target model";

        private readonly IStageResolver<SelectionRequest<ModelData>, DataBinding<ModelData>> _soloFallback;

        public TacticianModelSelectionResolver(
            IStageResolver<SelectionRequest<ModelData>, DataBinding<ModelData>> soloFallback)
        {
            _soloFallback = soloFallback;
        }

        public Task<DataBinding<ModelData>> Resolve(SelectionRequest<ModelData> request)
        {
            if (request.Instructions == null || request.ValidOptions.Count == 0
                || !request.Instructions.EndsWith(ModelPickInstructionSuffix, StringComparison.Ordinal))
                return _soloFallback.Resolve(request);

            DataBinding<ModelData> best = request.ValidOptions[0].Option;
            float bestScore = float.NegativeInfinity;
            foreach (SelectionRequest<ModelData>.ValidOption option in request.ValidOptions)
            {
                ModelData model = option.Option.GetValue();
                float score = ModelOutputValue(model)
                    + (model.RuleDefinitions.Any() ? TacticianWeights.SnipeSpecialModelBonus : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = option.Option;
                }
            }
            return Task.FromResult(best);
        }

        // The A4-4 output currency: raw attacks weighted up by AP.
        private static float ModelOutputValue(ModelData model)
        {
            float output = 0f;
            foreach (Weapon weapon in model.Weapons)
                output += weapon.Attacks * (1f + 0.15f * weapon.ArmorPenetration);
            return output;
        }
    }
}
