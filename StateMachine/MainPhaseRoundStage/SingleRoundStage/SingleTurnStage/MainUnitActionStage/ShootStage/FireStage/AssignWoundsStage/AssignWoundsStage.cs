using FDG.StageResolution.Requests;
using FDG.Utilities;
using System;
using System.Threading.Tasks;

namespace FDG.Stages
{

    public class AssignWoundsStage<TMetadata> : CombatStage<AssignWoundsResults, AssignWoundsStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public AssignWoundsStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Action<AssignWoundsResults> onFinished)
        {
            metaData.QueryForResult(out RollToSaveResults rollToSaveResults);

            float totalWoundsDealt = 0;
            foreach (FailedSaveInfo failedSaves in rollToSaveResults.FailedSaveList)
            {
                totalWoundsDealt += failedSaves.SaveCount;
            }

            float defenderRemainingWounds = metaData.DefendingUnit.RemainingWounds();

            //If the opponent doesn't have to provide a choice, like if the unit will die or there's just one model, 
            //then just do it automatically.
            AssignWoundsResults assignWoundsResults;

            if(totalWoundsDealt == 0)
            {
                assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, 0);
                //Should be auto-filled regardless but just do it. 
                assignWoundsResults.AutoFill();
            }
            else if (totalWoundsDealt >= defenderRemainingWounds)
            {
                //We've killed off the unit. No need to use the handler to ask what will die.
                //Fill results with wounds it would take to kill.
                //TODO: Would be cool to list overkill amount somewhere besides text log.
                assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, defenderRemainingWounds);
                assignWoundsResults.AutoFill();

                float overkill = totalWoundsDealt - defenderRemainingWounds;
                string pluralizedWound = defenderRemainingWounds == 1 ? "wound" : "wounds";
                GameContext.Log($"Assigning {defenderRemainingWounds} {pluralizedWound} (Overkill: {overkill})");
            }
            else if (metaData.DefendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive())
                .Count() == 1)
            {
                //If we only have one living model, just autoresolve it.
                assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, defenderRemainingWounds);
                assignWoundsResults.AutoFill();
            }
            else
            {
                //TODO: Add nuance of applying wounds to existing models with tough before others.
                //I'm also putting this TODO in the results class.
                //assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, totalWoundsDealt);
                AssignWoundsRequest request = new AssignWoundsRequest(metaData.DefendingUnit.PlayerID(), "Assign Wounds", 
                    metaData.DefendingUnit, totalWoundsDealt);
                assignWoundsResults = await metaData.GameContext.PlayerRequester()
                    .RequestDecision<AssignWoundsRequest, AssignWoundsResults>(request);
                //throw new NotImplementedException();
                //GameContext.GetHandler<IAssignWoundsHandler>().Handle(metaData.DefendingUnit, assignWoundsResults, () => OnHandled(assignWoundsResults, onFinished));
            }

            onFinished(assignWoundsResults);
        }

        /*
        private void OnHandled(AssignWoundsResults woundsResults, Action<AssignWoundsResults> onFinished)
        {
            if (woundsResults.IsFinishedAssigning == false)
            {
                throw new InvalidOperationException($"Called assigning wounds finished when it was not finished. " +
                    $"Wounds to assign: {woundsResults.TotalWoundsToAssign} Wounds assigned: {woundsResults.TotalAssignedWounds}.");
            }

            onFinished(woundsResults);
        }
        */
    }
}