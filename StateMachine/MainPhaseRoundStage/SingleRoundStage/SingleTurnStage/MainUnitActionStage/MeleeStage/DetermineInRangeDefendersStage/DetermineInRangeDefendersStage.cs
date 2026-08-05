using FDG.Data;

namespace FDG.Stages
{
    public class DetermineInRangeDefendersStage : StageBase<ICombatActionContext>
    {
        public const string DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION = "DetermineInRangeDefenderFinished";

        public StageBinding ToChooseMeleeWeapons;

        /// <summary>
        /// #355: the attacker is in contact with nothing to swing (an impact-only charger, or a unit whose
        /// melee-armed models died), so the weapon offer is skipped and the melee goes straight to the
        /// strike-back the defender is owed. This stage owns the branch because it is the last one to run
        /// before <c>ChooseMeleeWeaponStage</c>, which throws on an empty pool — and because the strike-back
        /// needs the in-range defenders THIS stage records.
        /// </summary>
        public StageBinding ToStrikeBackUnopposed;

        public DetermineInRangeDefendersStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToChooseMeleeWeapons = new StageBinding(this);
            ToStrikeBackUnopposed = new StageBinding(this);
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

            // #355: nothing to swing - skip the weapon offer (and the extra-attack window before it, which
            // is an extra ATTACK for a unit making none) and let the defender strike back.
            if (context.AvailableWeapons.Count == 0)
            {
                await ToStrikeBackUnopposed.Activate(context);
                return;
            }

            await ToChooseMeleeWeapons.Activate(context);
        }

    }
}
