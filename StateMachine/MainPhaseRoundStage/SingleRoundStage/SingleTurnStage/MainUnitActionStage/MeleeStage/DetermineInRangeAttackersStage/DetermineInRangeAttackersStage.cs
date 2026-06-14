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

            // No attacker can actually strike when the swing pool is empty — either no model is in melee
            // range, OR the in-range models carry no melee weapon (e.g. the melee-armed models died and only
            // ranged-only survivors remain in range). Either way, resolve the melee with no attacks rather
            // than feeding an empty pool into ChooseMeleeWeaponStage, which throws. SetInRangeAttackers has
            // already rebuilt AvailableWeapons from exactly the in-range living models, so it is the precise
            // precondition ChooseMeleeWeaponStage requires.
            if (context.AvailableWeapons.Count == 0)
            {
                GameContext.Log("No in-range attacking model has a melee weapon; melee resolves with no attacks.");
                await OnNoAttackersInRange.Activate(context);
                return;
            }

            await ToDetermineDefenders.Activate(context);
        }
    }
}
