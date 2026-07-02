using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #103 — the AI never spends its own spell tokens to boost or hinder another Caster's cast; it doesn't
    /// yet reason about whether the swing is worth the tokens, so it always declines (spends 0) rather than
    /// burning tokens on an arbitrary cast. Same conservative stance as the pre-attack ability skip. A real
    /// policy (protect a high-value cast / spoil a dangerous enemy one) is a future refinement.
    /// </summary>
    public class AiCastAssistResolver : IStageResolver<CastAssistRequest, int>
    {
        public Task<int> Resolve(CastAssistRequest request) => Task.FromResult(0);
    }
}
