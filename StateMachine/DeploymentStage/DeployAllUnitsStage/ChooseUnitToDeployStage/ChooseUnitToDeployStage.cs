

using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public class ChooseUnitToDeployStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;
        public ChooseUnitToDeployStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.Log("Entered Choose Unit to Deploy stage.");

            if(context.CurrentDeployingUnit != null)
            {
                //Technically not an issue here as we're about to set it ourselves. But if we aren't clearing this by the time
                //we get back to this, we could get some hard-to-debug issues.
                throw new InvalidOperationException($"Already had a unit chosen to activate when entering {nameof(ChooseUnitToDeployStage)}.");
            }

            PlayerID currentPlayerID = context.GetCurrentDeployingPlayerID();

            if (context.UndeployedUnits[currentPlayerID].Count == 0)
            {
                throw new InvalidOperationException($"Entered {nameof(ChooseUnitToDeployStage)} with active player ID " +
                    $"{currentPlayerID}, but that player is listed as not having any units left to deploy.");
            }

            List<SelectionRequest<UnitData>.ValidOption> validOptions = new List<SelectionRequest<UnitData>.ValidOption>();

            //We don't account for things that can't deploy until later, but I can't think of a reason that ever happens
            //in GDF. For Scout, it's optional, and Aircraft deploys first.

            foreach (DataBinding<UnitData> potentialUnit in context.UndeployedUnits[currentPlayerID])
            {
                var unitOption = new SelectionRequest<UnitData>.ValidOption(potentialUnit, potentialUnit.GetValue().Name);
                validOptions.Add(unitOption);
            }

            // Choosing which unit to deploy is mandatory — no back-destination, so no cancel.
            SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(currentPlayerID, "Choose Unit to Deploy",
                validOptions, new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: false);

            DataBinding<UnitData> chosenUnit = 
                await GameContext.PlayerRequester.RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>
                (request);

            context.Log($"Activating {chosenUnit.GetValue().Name}.");

            context.CurrentDeployingUnit = chosenUnit;
            context.UndeployedUnits[currentPlayerID].Remove(chosenUnit);

            OnFinish.Activate(context);
        }
    }
}
