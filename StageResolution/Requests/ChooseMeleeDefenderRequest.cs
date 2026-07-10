using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// The melee "which unit do I fight" decision - its own request type (the A4-1 pattern:
    /// resolvers dispatch on TYPE, not prompt text), split from the generic
    /// <see cref="CancellableSelectionRequest{T}"/> which also serves pre-attack targeting (#100).
    /// The Tactician replaces exactly this decision (#191 A4-3).
    /// </summary>
    public class ChooseMeleeDefenderRequest : CancellableSelectionRequest<UnitData>
    {
        public ChooseMeleeDefenderRequest(PlayerID targetPlayerID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
            : base(targetPlayerID, instructions, validOptions, invalidOptions)
        {
        }

        [JsonConstructor]
        public ChooseMeleeDefenderRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
            : base(targetPlayerID, taskID, instructions, validOptions, invalidOptions)
        {
        }
    }
}
