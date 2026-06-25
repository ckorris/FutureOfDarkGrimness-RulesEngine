

using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
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

            ITeam deployingTeam = context.FirstDeploymentRollOrder[context.CurrentDeployingTeamIndex];

            DataBinding<RectangularZone> deploymentZone = context.PlayerDeploymentZones[deployingTeam];

            var placeObjectsRequest = new PlaceObjectsRequest<ModelData>(currentPlayerID, "Place Unit Models",
                deploymentZone.GetValue(), deployingUnit.ModelBindings);

            List<PlacedObjectEntry<ModelData>> modelPositions = await GameContext.PlayerRequester.RequestDecision
                <PlaceObjectsRequest<ModelData>, List<PlacedObjectEntry<ModelData>>>(placeObjectsRequest);

            //Actually place the objects.
            foreach(PlacedObjectEntry<ModelData> entry in modelPositions)
            {
                entry.Binding.GetValue().SetPosition(entry.Position);
            }

            await OfferPostDeploymentAbilities(deployingUnit, currentPlayerID);

            context.CurrentDeployingUnit = null; //Cleanup.

            await OnFinish.Activate(context);
        }

        /// <summary>
        /// Fires the Deployment_OnUnitDeployed "when" for the just-deployed unit and offers any
        /// activated abilities triggered there (Vanguard's reposition). For each offer the owning
        /// player accepts, the resolved operation queue is enacted through the engine's imperative-op
        /// executor — the movement subsystem, for a triggered move. First production use of the
        /// GatherOffers / ResolveAbility / OperationExecutor chain.
        /// </summary>
        private async Task OfferPostDeploymentAbilities(UnitData deployedUnit, PlayerID owningPlayer)
        {
            var deployedContext = new UnitDeployedContext(deployedUnit);

            foreach (AbilityOffer offer in GameContext.RuleEvaluator.GatherOffers(deployedContext))
            {
                var question = new YesNoRequest(owningPlayer, $"Use {offer.RuleName} on {deployedUnit.Name}?", aiPrefersYes: true);
                bool accepted = await GameContext.PlayerRequester
                    .RequestDecision<YesNoRequest, bool>(question);

                if (!accepted) continue;

                GameContext.Log($"{deployedUnit.Name} used {offer.RuleName}.");

                // Corpus deployment abilities are all Self-targeted (Vanguard) — the bearer is the
                // target. Foe/Friend target selection (none at this hook yet) lands with the next
                // activated-ability slice.
                IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                    .ResolveAbility(offer, new[] { (IUnit)deployedUnit });

                // Close the cost gate (Vanguard's once-per-game marker) — OperationExecutor runs only the
                // imperative ExecutableOperations, so the token grant must be applied separately.
                OperationApplier.ApplyTokenOperations(ops);

                await OperationExecutor.Execute(ops, new GameOperationServices(GameContext));
            }
        }
    }
}
