using FDG.Data;

namespace FDG.StageResolution.Requests
{
    public class ChooseMeleeDefenderRequest : SelectionRequest<UnitData>
    {
        public DataBinding<UnitData> AttackingUnit { get; }

        public ChooseMeleeDefenderRequest(PlayerID targetPlayerID, string instructions,
            DataBinding<UnitData> attackingUnit,
            IReadOnlyList<ValidOption> validOptions,
            IReadOnlyList<InvalidOption> invalidOptions)
            : base(targetPlayerID, instructions, validOptions, invalidOptions)
        {
            AttackingUnit = attackingUnit;
        }
    }
}
