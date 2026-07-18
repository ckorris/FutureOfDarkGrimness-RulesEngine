using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #244 — solo-rules spell pick: the first castable spell, no self-boost. Mirrors the old
    /// first-option StringSelectionRequest default (pinned by the benchmark hashes), and never
    /// cancels — a cancelled pick loops straight back to Choose Action with nothing spent, the
    /// forced-Cast livelock class. A boost policy (spend when the odds are worth it) is a future
    /// refinement, same bucket as the assist policy in <see cref="AiCastAssistResolver"/>.
    /// </summary>
    public class AiChooseSpellResolver : IStageResolver<ChooseSpellRequest, ChooseSpellReply>
    {
        public Task<ChooseSpellReply> Resolve(ChooseSpellRequest request)
        {
            for (int i = 0; i < request.Spells.Count; i++)
            {
                if (request.Spells[i].Castable)
                    return Task.FromResult(new ChooseSpellReply(i, 0));
            }
            // Unreachable in practice — the stage only raises the request when a castable spell exists.
            return Task.FromResult(ChooseSpellReply.Cancel);
        }
    }
}
