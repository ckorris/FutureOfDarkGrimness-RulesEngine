

using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public class DeployUnitStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;

        public DeployUnitStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.Log("Entered Deploy Unit stage.");

            if(context.CurrentDeployingUnit == null)
            {
                throw new InvalidOperationException($"No unit chosen in context when entering {nameof(DeployUnitStage)}.");
            }

            PlayerID currentPlayerID = context.GetCurrentDeployingPlayerID();
            UnitData deployingUnit = context.CurrentDeployingUnit.GetValue();

            if(deployingUnit.PlayerID != currentPlayerID)
            {
                throw new InvalidOperationException($"Entered {nameof(DeployUnitStage)} with a unit chosen that doesn't belong to the current player." + 
                    $"Unit: {deployingUnit.Name} owned by PlayerID {deployingUnit.PlayerID}. Current player: {currentPlayerID}.");
            }

            var placeObjectsRequest = new PlaceObjectsRequest<ModelData>(currentPlayerID, "Place Unit Models",
                deployingUnit.ModelBindings);

            Dictionary<DataBinding<ModelData>, Position> modelPositions = await GameContext.PlayerRequester.RequestDecision
                <PlaceObjectsRequest<ModelData>, Dictionary<DataBinding<ModelData>, Position>>(
                currentPlayerID, placeObjectsRequest);

            //Actually place the objects.
            foreach(KeyValuePair<DataBinding<ModelData>, Position> kvp in modelPositions)
            {
                kvp.Key.GetValue().SetPosition(kvp.Value);
            }

            context.CurrentDeployingUnit = null; //Cleanup.

            OnFinish.Activate(context);
        }
    }
}
