using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Answers Choose Action by planning the whole activation (#191 A4-2): the planner enumerates
    /// macro-actions, scores (action x macro-action) pairs, caches the winner, and this resolver
    /// returns its action. The deployment hold-or-deploy prompt (#191 A5-2) routes to
    /// AmbushPolicy. Every OTHER string selection - pre-attack menus - delegates to the unmodified
    /// solo-rules resolver (G3 fallback), as does any request the planner declines (its choice not
    /// valid, no known active unit). The spell picker moved to its own request type (#244,
    /// <see cref="TacticianChooseSpellResolver"/>), so it no longer rides through here.
    /// <para>
    /// Choose Action is its own request type since #191 B1 step 5a (<see cref="ChooseActionRequest"/>) -
    /// the follow-up this doc comment used to record. Hold-or-deploy and everything else still ride
    /// <see cref="StringSelectionRequest"/>.
    /// </para>
    /// </summary>
    public class TacticianActionResolver : IStageResolver<StringSelectionRequest, string>,
        IStageResolver<ChooseActionRequest, string>
    {
        // ChooseUnitToDeployStage.PromptHoldOrDeploy's instructions:
        // "Deploy {unit} now, or hold it in {rule}?" - the unit's name is only carried there.
        private const string HoldPromptPrefix = "Deploy ";
        private const string HoldPromptMarker = " now, or hold it in ";
        private const string HoldOptionPrefix = "Hold in ";

        private readonly TacticianPlanner _planner;
        private readonly ITableState _tableState;
        private readonly FDG.Ai.Resolvers.AiStringSelectionResolver _soloFallback;

        public TacticianActionResolver(TacticianPlanner planner, ITableState tableState,
            FDG.Ai.Resolvers.AiStringSelectionResolver soloFallback)
        {
            _planner = planner;
            _tableState = tableState;
            _soloFallback = soloFallback;
        }

        public Task<string> Resolve(ChooseActionRequest request)
        {
            string? planned = _planner.ChooseAction(request.ValidOptions);
            return planned != null ? Task.FromResult(planned) : _soloFallback.Resolve(request);
        }

        public Task<string> Resolve(StringSelectionRequest request)
        {
            if (request.ValidOptions.Contains(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE)
                && request.ValidOptions.Any(o => o.StartsWith(HoldOptionPrefix, StringComparison.Ordinal)))
            {
                // A5-2: hold melee/short-range Ambushers, deploy shooters. Answered explicitly both
                // ways (never "Back to unit list" - that would loop the deploy picker forever).
                string? unitName = ParseHoldPromptUnitName(request.Instructions);
                string choice = unitName != null
                    && AmbushPolicy.ShouldHold(_tableState, request.TargetPlayerID, unitName)
                    ? request.ValidOptions.First(o => o.StartsWith(HoldOptionPrefix, StringComparison.Ordinal))
                    : ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE;
                return Task.FromResult(choice);
            }
            return _soloFallback.Resolve(request);
        }

        private static string? ParseHoldPromptUnitName(string instructions)
        {
            if (!instructions.StartsWith(HoldPromptPrefix, StringComparison.Ordinal)) return null;
            int marker = instructions.IndexOf(HoldPromptMarker, StringComparison.Ordinal);
            return marker <= HoldPromptPrefix.Length ? null
                : instructions[HoldPromptPrefix.Length..marker];
        }
    }
}
