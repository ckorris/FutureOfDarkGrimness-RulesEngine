

using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.StageResolution;
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
            context.LogDebug("Entered Deploy Unit stage.");

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

            List<PlacedObjectEntry<ModelData>> modelPositions = await PlacementRequesting
                .RequestMandatoryPlacement(GameContext.PlayerRequester, placeObjectsRequest);

            //Actually place the objects.
            foreach(PlacedObjectEntry<ModelData> entry in modelPositions)
            {
                entry.Binding.GetValue().SetPosition(entry.Position);
                if (entry.Facing.HasValue) entry.Binding.GetValue().SetFacing(entry.Facing.Value);
            }

            // On the table is the negation of in reserve. A freshly deployed unit never carries the token,
            // but a legacy save's bootstrap may have stamped it (see GameSaveSerializer.StampLegacyReserves).
            Rules.Dispatch.ReserveRules.ClearReserve(deployingUnit);

            await OfferPostDeploymentAbilities(deployingUnit, currentPlayerID);

            context.CurrentDeployingUnit = null; //Cleanup.

            await OnFinish.Activate(context);
        }

        /// <summary>
        /// Fires the Deployment_OnUnitDeployed "when" for the just-deployed unit and offers any
        /// activated abilities triggered there. For each one enacted, the resolved operation queue runs:
        /// imperative ops through the engine's executor (the movement subsystem, for a triggered move) and
        /// any <see cref="RuleOperation.RepositionModels"/> folded into a placement (shared with
        /// <c>ActivationStartStage</c>). First production use of the GatherOffers / ResolveAbility /
        /// OperationExecutor chain.
        ///
        /// <para>Two shapes hang here, told apart by how many abilities the rule offers. A single ability is
        /// the "you MAY" shape (Vanguard's reposition move, Fanatic's reposition placement) and keeps its
        /// Yes/No prompt. SEVERAL abilities is the "pick one effect" shape (Versatile Defense, whose text
        /// reads "when this unit is deployed or activated, pick one effect"), which is mandatory and routes
        /// through <see cref="AbilityEffectChoice"/> — the same resolution <c>ActivationStartStage</c>
        /// uses, so the rule behaves identically at both of its trigger hooks.</para>
        /// </summary>
        private async Task OfferPostDeploymentAbilities(UnitData deployedUnit, PlayerID owningPlayer)
        {
            var deployedContext = new UnitDeployedContext(deployedUnit);

            foreach (IReadOnlyList<AbilityOffer> ruleOffers in AbilityEffectChoice.GroupByRule(
                         GameContext.RuleEvaluator.GatherOffers(deployedContext)))
            {
                if (ruleOffers.Count == 1)
                {
                    AbilityOffer offer = ruleOffers[0];
                    var question = new YesNoRequest(owningPlayer,
                        $"Use {offer.RuleName} on {deployedUnit.Name}?", defaultAnswer: true);
                    bool accepted = await GameContext.PlayerRequester
                        .RequestDecision<YesNoRequest, bool>(question);

                    if (!accepted) continue;

                    GameContext.Log($"{deployedUnit.Name} used {offer.RuleName}.");
                }

                // Corpus deployment abilities are all Self-targeted — the bearer is the target. Foe/Friend
                // target selection (none at this hook yet) lands with the next activated-ability slice.
                // Resolve also closes the cost gate (Vanguard's once-per-game marker) and runs the executor.
                AbilityEffectChoice.Outcome outcome = await AbilityEffectChoice.Resolve(
                    GameContext, owningPlayer, deployedUnit, ruleOffers, "on deployment");

                if (ruleOffers.Count > 1)
                {
                    GameContext.Log($"{deployedUnit.Name}: {outcome.Chosen.RuleName} - " +
                        $"chose {outcome.Chosen.Ability.Label}.");
                }

                // Fanatic emits a RepositionModels op the executor ignores (it is stage-folded, not
                // executable). Fold it into a within-radius placement, exactly as ActivationStartStage does.
                await RepositionPlacement.OfferFromOperations(GameContext, deployedUnit, outcome.Operations);
            }
        }
    }
}
