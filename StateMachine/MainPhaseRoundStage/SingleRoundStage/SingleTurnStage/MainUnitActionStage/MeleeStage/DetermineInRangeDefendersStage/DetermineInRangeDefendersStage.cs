using FDG.Data;

namespace FDG.Stages
{
    public class DetermineInRangeDefendersStage : StageBase<ICombatActionContext>
    {
        public const string DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION = "DetermineInRangeDefenderFinished";

        public StageBinding ToChooseMeleeWeapons;

        public DetermineInRangeDefendersStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToChooseMeleeWeapons = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            // #017: record which defending models are within melee range of an attacker; these are the
            // models eligible to strike back (consumed by StrikeBackStage).
            IReadOnlyList<DataBinding<ModelData>> defenderModels = context.DefendingUnit.GetValue().ModelBindings;
            IReadOnlyList<DataBinding<ModelData>> attackerModels = context.AttackingUnit.GetValue().ModelBindings;

            List<DataBinding<ModelData>> inRange = MeleeRangeUtilities.GetModelsInMeleeRange(defenderModels, attackerModels);
            context.SetInRangeDefenders(inRange);

            GameContext.Log($"Determine In Range Defenders: {inRange.Count} of {defenderModels.Count} models in melee range.");
            await ToChooseMeleeWeapons.Activate(context);
        }

    }
}
