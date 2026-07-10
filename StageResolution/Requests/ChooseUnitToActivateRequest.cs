using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// The per-turn "which unit activates next" decision - its own request type rather than a bare
    /// <see cref="SelectionRequest{T}"/> of UnitData, so resolvers dispatch on TYPE instead of
    /// prompt text: the Tactician replaces exactly this decision (#191 A4-1) while spell-target,
    /// embark, and deployment selections keep their generic handling. Same reasoning as
    /// <see cref="ChooseAbilityEffectRequest"/> (#197 P5a). Mandatory - activation has no
    /// back-destination, so no cancel.
    /// </summary>
    public class ChooseUnitToActivateRequest : SelectionRequest<UnitData>
    {
        public ChooseUnitToActivateRequest(PlayerID targetPlayerID,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
            : base(targetPlayerID, "Choose Unit to Activate", validOptions, invalidOptions,
                allowCancel: false)
        {
        }

        [JsonConstructor]
        public ChooseUnitToActivateRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions,
            bool allowCancel)
            : base(targetPlayerID, taskID, instructions, validOptions, invalidOptions, allowCancel)
        {
        }
    }
}
