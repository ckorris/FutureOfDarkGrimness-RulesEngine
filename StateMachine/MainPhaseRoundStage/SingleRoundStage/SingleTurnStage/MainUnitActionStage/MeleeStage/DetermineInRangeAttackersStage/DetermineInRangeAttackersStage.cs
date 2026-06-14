using FDG.Data;

namespace FDG.Stages
{
    public class DetermineInRangeAttackersStage : StageBase<ICombatActionContext>
    {
        public StageBinding ToDetermineDefenders;
        public StageBinding OnNoAttackersInRange;

        public DetermineInRangeAttackersStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToDetermineDefenders = new StageBinding(this);
            OnNoAttackersInRange = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            // #017: after pile-in, only attacking models within melee range of an enemy model may strike.
            IReadOnlyList<DataBinding<ModelData>> attackerModels = context.AttackingUnit.GetValue().ModelBindings;
            IReadOnlyList<DataBinding<ModelData>> defenderModels = context.DefendingUnit.GetValue().ModelBindings;

            List<DataBinding<ModelData>> inRange = MeleeRangeUtilities.GetModelsInMeleeRange(attackerModels, defenderModels);
            context.SetInRangeAttackers(inRange);

            GameContext.Log($"Determine In Range Attackers: {inRange.Count} of {attackerModels.Count} models in melee range.");

            // Defense-in-depth: with the dead-model fix to the charge/defender gates this shouldn't happen in
            // normal play, but if no attacking model is in range (e.g. a future vertical or charge-move gap)
            // the melee resolves with no attacks rather than feeding an empty pool into ChooseMeleeWeaponStage.
            if (inRange.Count == 0)
            {
                GameContext.Log("No attacking models in melee range; melee resolves with no attacks.");
                await OnNoAttackersInRange.Activate(context);
                return;
            }

            await ToDetermineDefenders.Activate(context);
        }
    }
}
