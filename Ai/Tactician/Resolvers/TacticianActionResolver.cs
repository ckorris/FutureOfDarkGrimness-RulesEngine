using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Answers Choose Action by planning the whole activation (#191 A4-2): the planner enumerates
    /// macro-actions, scores (action x macro-action) pairs, caches the winner, and this resolver
    /// returns its action. The cast stage's spell picker (#191 A5) routes to the planner's
    /// value-maximizing pick. Every OTHER string selection - pre-attack menus, ambush
    /// hold-or-deploy - delegates to the unmodified solo-rules resolver (G3 fallback), as does any
    /// request the planner declines (its choice not valid, no known active unit).
    /// <para>
    /// Dispatch is on the request's Instructions ("Choose Action") - the same key the solo
    /// resolver has always used for this request type, unlike A4-1's TaskName mistake. Splitting
    /// ChooseActionRequest into its own type (like ChooseUnitToActivateRequest) is a recorded
    /// candidate follow-up.
    /// </para>
    /// </summary>
    public class TacticianActionResolver : IStageResolver<StringSelectionRequest, string>
    {
        // CastSpellStage.PickSpell's instructions ("Choose a spell to cast - {caster} has N spell
        // tokens"); the trailing half varies, so dispatch is on the stable prefix.
        public const string SpellPickInstructionPrefix = "Choose a spell to cast";

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
            else if (request.Instructions.StartsWith(SpellPickInstructionPrefix, StringComparison.Ordinal))
            {
                string? spell = _planner.ChooseSpell(request.ValidOptions);
                if (spell != null)
                    return Task.FromResult(spell);
            }
            return _soloFallback.Resolve(request);
        }
    }
}
