using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Solo-rules answer to "pick one of this rule's effects" (#197 P5a): always the first option.
    ///
    /// Deliberately a placeholder, and deliberately its own resolver rather than a branch inside
    /// <see cref="AiStringSelectionResolver"/>. The bot has no way to compare "AP(+1)" against "+1 to hit"
    /// without knowing what it is about to attack, and this hook fires before an action — let alone a target
    /// — has been chosen. Picking the first authored effect is at least stable and seeded-transcript
    /// reproducible; guessing would be noise. Same conservative stance as the pre-attack menu's "skip".
    ///
    /// The Tactician (#191) replaces resolvers one request type at a time; because this decision has its own
    /// request type, a real policy can be swapped in here alone, and can recover the full ability definitions
    /// (and their effect graphs) from the rule catalog via <see cref="ChooseAbilityEffectRequest.RuleName"/>.
    /// </summary>
    public class AiChooseAbilityEffectResolver : IStageResolver<ChooseAbilityEffectRequest, int>
    {
        public Task<int> Resolve(ChooseAbilityEffectRequest request)
        {
            return Task.FromResult(0);
        }
    }
}
