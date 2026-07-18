using System.Collections.Generic;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// #191 A5 / #243 — answers the dedicated spell picker with the planner's highest-net-value
    /// castable spell (see <see cref="TacticianPlanner.ChooseSpell"/>), never Cancel (the
    /// forced-Cast livelock class). No self-boost yet — a boost policy is a recorded refinement,
    /// same bucket as the assist policy. Falls back to the solo first-castable pick when the
    /// planner has no claim (unknown active unit / army).
    /// </summary>
    public class TacticianChooseSpellResolver : IStageResolver<ChooseSpellRequest, ChooseSpellReply>
    {
        private readonly TacticianPlanner _planner;
        private readonly IStageResolver<ChooseSpellRequest, ChooseSpellReply> _soloFallback;

        public TacticianChooseSpellResolver(TacticianPlanner planner,
            IStageResolver<ChooseSpellRequest, ChooseSpellReply> soloFallback)
        {
            _planner = planner;
            _soloFallback = soloFallback;
        }

        public Task<ChooseSpellReply> Resolve(ChooseSpellRequest request)
        {
            // The planner values spells by their picker label ("Name (cost)") — hand it the castable
            // rows' labels and map its pick back to the request index.
            List<string> castableLabels = new List<string>();
            foreach (ChooseSpellRequest.SpellOption option in request.Spells)
            {
                if (option.Castable) castableLabels.Add(option.Label);
            }

            string? pick = _planner.ChooseSpell(castableLabels);
            if (pick != null)
            {
                for (int i = 0; i < request.Spells.Count; i++)
                {
                    if (request.Spells[i].Castable && request.Spells[i].Label == pick)
                        return Task.FromResult(new ChooseSpellReply(i, 0));
                }
            }
            return _soloFallback.Resolve(request);
        }
    }
}
