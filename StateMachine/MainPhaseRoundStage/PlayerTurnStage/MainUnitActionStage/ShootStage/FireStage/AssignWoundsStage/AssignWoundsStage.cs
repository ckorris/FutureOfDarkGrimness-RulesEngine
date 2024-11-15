using System;

namespace FDG.Stages
{

    public class AssignWoundsStage : CombatStage<AssignWoundsResults, AssignWoundsStage, ICombatMetadata>
    {
        public AssignWoundsStage(StateMachine stateMachine, ISingleAttackContext<ICombatMetadata> context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<AssignWoundsResults> onFinished)
        {
            metaData.QueryForResult(out RollToSaveResults rollToSaveResults);

            float totalWoundsDealt = 0;
            foreach (FailedSaveInfo failedSaves in rollToSaveResults.FailedSaveList)
            {
                totalWoundsDealt += failedSaves.SaveCount;
            }

            float defenderRemainingWounds = metaData.DefendingUnit.RemainingWounds;

            if (totalWoundsDealt >= defenderRemainingWounds)
            {
                //We've killed off the unit. No need to use the handler to ask what will die.
                //Fill results with wounds it would take to kill.
                //TODO: Would be cool to list overkill amount somewhere besides text log.
                AssignWoundsResults assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, defenderRemainingWounds);
                assignWoundsResults.AutoFill();

                float overkill = totalWoundsDealt - defenderRemainingWounds;
                string pluralizedWound = defenderRemainingWounds == 1 ? "wound" : "wounds";
                Context.Log($"Assigning {defenderRemainingWounds} {pluralizedWound} (Overkill: {overkill})");
                onFinished(assignWoundsResults);
            }
            else
            {
                //TODO: Add nuance of applying wounds to existing models with tough before others.
                //I'm also putting this TODO in the results class.
                AssignWoundsResults assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, totalWoundsDealt);

                Context.GetHandler<IAssignWoundsHandler>().Handle(metaData.DefendingUnit, assignWoundsResults, () => OnHandled(assignWoundsResults, onFinished));
            }
        }

        private void OnHandled(AssignWoundsResults woundsResults, Action<AssignWoundsResults> onFinished)
        {
            if (woundsResults.IsFinishedAssigning == false)
            {
                throw new InvalidOperationException($"Called assigning wounds finished when it was not finished. " +
                    $"Wounds to assign: {woundsResults.TotalWoundsToAssign} Wounds assigned: {woundsResults.TotalAssignedWounds}.");
            }

            onFinished(woundsResults);
        }
    }

    public interface IAssignWoundsHandler
    {
        /// <summary>
        /// When called, you must assign a wound on <paramref name="woundsResults"/> until its 
        /// <see cref="AssignWoundsResults.TotalAssignedWounds"/> is equal to <see cref="AssignWoundsResults.TotalWoundsToAssign"/> value.
        /// </summary>
        /// <param name="defendingUnit">Unit to which we're assigning wounds.</param>
        /// <param name="woundsResults">Wounds class used to assign wounds.</param>
        /// <param name="onWoundsAssigned">Call this when finished assigning wounds.</param>
        public void Handle(IUnit defendingUnit, AssignWoundsResults woundsResults, Action onWoundsAssigned);
    }
}