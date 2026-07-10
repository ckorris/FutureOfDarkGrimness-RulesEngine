using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Answers Choose Action by planning the whole activation (#191 A4-2): the planner enumerates
    /// macro-actions, scores (action x macro-action) pairs, caches the winner, and this resolver
    /// returns its action. Every OTHER string selection - pre-attack menus, ambush hold-or-deploy,
    /// spell picks - delegates to the unmodified solo-rules resolver (G3 fallback), as does any
    /// Choose Action the planner declines (its choice not valid, no known active unit).
    /// <para>
    /// Dispatch is on the request's Instructions ("Choose Action") - the same key the solo
    /// resolver has always used for this request type, unlike A4-1's TaskName mistake. Splitting
    /// ChooseActionRequest into its own type (like ChooseUnitToActivateRequest) is a recorded
    /// candidate follow-up.
    /// </para>
    /// </summary>
    public class TacticianActionResolver : IStageResolver<StringSelectionRequest, string>
    {
        private readonly TacticianPlanner _planner;
        private readonly IStageResolver<StringSelectionRequest, string> _soloFallback;

        public TacticianActionResolver(TacticianPlanner planner,
            IStageResolver<StringSelectionRequest, string> soloFallback)
        {
            _planner = planner;
            _soloFallback = soloFallback;
        }

        public Task<string> Resolve(StringSelectionRequest request)
        {
            if (request.Instructions == "Choose Action")
            {
                string? planned = _planner.ChooseAction(request.ValidOptions);
                if (planned != null)
                    return Task.FromResult(planned);
            }
            return _soloFallback.Resolve(request);
        }
    }
}
