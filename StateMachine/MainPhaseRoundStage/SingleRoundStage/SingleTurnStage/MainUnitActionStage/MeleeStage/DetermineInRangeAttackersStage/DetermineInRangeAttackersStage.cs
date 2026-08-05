using FDG.Data;

namespace FDG.Stages
{
    public class DetermineInRangeAttackersStage : StageBase<ICombatActionContext>
    {
        public StageBinding ToDetermineDefenders;
        public StageBinding OnNoAttackersInRange;

        /// <summary>
        /// #345: models ARE in contact, they just carry nothing to swing — an impact-only charger (every
        /// APC/tank/speeder), or a unit whose melee-armed models all died. Distinct from
        /// <see cref="OnNoAttackersInRange"/>, which means nobody reached contact at all (the defender was
        /// wiped by the impact hits, or pile-in failed): there the melee is over, whereas here two units
        /// are locked together and the melee must still resolve — the defender strikes back if it can, the
        /// winner is decided on wounds dealt, and the loser tests morale.
        /// </summary>
        public StageBinding OnAttackersInRangeUnarmed;

        public DetermineInRangeAttackersStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToDetermineDefenders = new StageBinding(this);
            OnNoAttackersInRange = new StageBinding(this);
            OnAttackersInRangeUnarmed = new StageBinding(this);
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
            //
            // #345 splits the two: whether anyone is IN CONTACT decides whether there is still a melee to
            // resolve. Nobody in contact (defender wiped by the impact hits, pile-in failed) ends it; models
            // in contact with nothing to swing — an impact-only charger, or a unit that lost its melee-armed
            // models — is still a melee, so it goes on to the strike-back the defender is owed.
            if (context.AvailableWeapons.Count == 0)
            {
                if (inRange.Count == 0)
                {
                    GameContext.Log("No attacking model reached melee range; the melee ends with no attacks.");
                    await OnNoAttackersInRange.Activate(context);
                    return;
                }

                GameContext.Log("No in-range attacking model has a melee weapon; " +
                                "the melee resolves with no attacks from the attacker.");
                await OnAttackersInRangeUnarmed.Activate(context);
                return;
            }

            await ToDetermineDefenders.Activate(context);
        }
    }
}
