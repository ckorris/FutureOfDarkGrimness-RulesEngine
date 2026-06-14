using FDG.Data;
using FDG.Utilities;
using System;

namespace FDG.Stages
{
    /// <summary>
    /// Applies the consequence of a *failed* melee morale test. Only reached on
    /// <see cref="RollForMoraleStage.OnMoraleFailed"/>. Per the GDF rules a unit that fails
    /// its morale test becomes Shaken — or, if it is already at half strength or less, it is
    /// Routed (removed from play) instead.
    ///
    /// Routing has no dedicated removal primitive in the engine; a destroyed unit is
    /// represented as "all models dead" (such units are filtered out of activation, turn
    /// order, and objective scoring everywhere via <c>GetIsAlive</c>). So a Rout is applied
    /// by dealing lethal wounds to every living model. Shaken is applied as a
    /// <see cref="TokenClearTrigger.ManualOnly"/> token; its idle-activation behavior and
    /// clearing are owned by the Shaken-activation work (#008), because a unit can become
    /// Shaken on its own activation and must persist Shaken into its next one.
    /// </summary>
    public class AssignMeleeMoralePenaltyStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnAssignedPenalty;

        public AssignMeleeMoralePenaltyStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnAssignedPenalty = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.QueryForResult(out DetermineMoraleSaveNeededResult moraleSaveNeeded) == false)
            {
                throw new InvalidOperationException($"{nameof(AssignMeleeMoralePenaltyStage)} reached but there was no " +
                    $"{nameof(DetermineMoraleSaveNeededResult)} in the context metadata.");
            }

            DataBinding<UnitData> losingUnit = moraleSaveNeeded.LosingUnit;

            if (losingUnit.GetValue().GetIsAtHalfStrength())
            {
                MoraleUtilities.Rout(losingUnit);
                GameContext.Log($"{losingUnit.Name()} failed its morale test at half strength and is Routed.");
            }
            else
            {
                MoraleUtilities.ApplyShaken(losingUnit);
                GameContext.Log($"{losingUnit.Name()} failed its morale test and is now Shaken.");
            }

            await OnAssignedPenalty.Activate(context);
        }
    }
}
