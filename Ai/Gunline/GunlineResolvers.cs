using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Gunline
{
    /// <summary>
    /// Activation order for the Gunline script: solo-style first-in-list (a human gunline has no
    /// urgent sequencing - every unit does the same thing), but announced to the planner so the
    /// action script knows whose activation it is.
    /// </summary>
    public class GunlineActivationResolver : IStageResolver<ChooseUnitToActivateRequest, DataBinding<UnitData>>
    {
        private readonly GunlinePlanner _planner;

        public GunlineActivationResolver(GunlinePlanner planner)
        {
            _planner = planner;
        }

        public Task<DataBinding<UnitData>> Resolve(ChooseUnitToActivateRequest request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException(
                    $"AI received a {nameof(ChooseUnitToActivateRequest)} with no valid options.");
            DataBinding<UnitData> pick = request.ValidOptions[0].Option;
            _planner.BeginActivation(pick);
            return Task.FromResult(pick);
        }
    }

    /// <summary>
    /// Choose Action via the Gunline script (its own request type since #191 B1 step 5a); every
    /// other string selection (pre-attack menus, deploy-or-hold prompts) delegates to the
    /// unmodified solo-rules resolver. The script never holds units in Ambush - a defensive line
    /// deploys everything and waits.
    /// </summary>
    public class GunlineActionResolver : IStageResolver<StringSelectionRequest, string>,
        IStageResolver<ChooseActionRequest, string>
    {
        private readonly GunlinePlanner _planner;
        private readonly FDG.Ai.Resolvers.AiStringSelectionResolver _soloFallback;

        public GunlineActionResolver(GunlinePlanner planner,
            FDG.Ai.Resolvers.AiStringSelectionResolver soloFallback)
        {
            _planner = planner;
            _soloFallback = soloFallback;
        }

        public Task<string> Resolve(ChooseActionRequest request)
        {
            string? scripted = _planner.ChooseAction(request.ValidOptions);
            return scripted != null ? Task.FromResult(scripted) : _soloFallback.Resolve(request);
        }

        public Task<string> Resolve(StringSelectionRequest request) => _soloFallback.Resolve(request);
    }
}
