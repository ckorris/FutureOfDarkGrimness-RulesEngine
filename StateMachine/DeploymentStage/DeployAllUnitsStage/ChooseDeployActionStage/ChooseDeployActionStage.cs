using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public class ChooseDeployActionStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;

        // The deploying unit was loaded into a transport instead of being placed (#035 slice B). Like an
        // Ambush hold, it counts as this player's deployment for the turn but is never placed (it stays
        // off-table inside the transport), so we skip the placement stage and go straight back to picking
        // the next player.
        public StageBinding OnEmbarked;

        public ChooseDeployActionStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
            OnEmbarked = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.LogDebug("Entering Choose Deploy Action stage.");

            if (context.CurrentDeployingUnit == null)
            {
                throw new InvalidOperationException(
                    $"No unit chosen in context when entering {nameof(ChooseDeployActionStage)}.");
            }

            UnitData deployingUnit = context.CurrentDeployingUnit.GetValue();

            // Transport (#035 slice B): if a friendly transport is already on the table with room for this
            // unit, the owner may load it in now instead of placing it. The unit is set aside off-table (an
            // EmbarkedIn token) and its placement stage is skipped — mirroring the Ambush hold flow. When no
            // transport is eligible, there is no prompt and we deploy normally exactly as before.
            List<DataBinding<UnitData>> transports = GetEligibleTransports(deployingUnit);
            if (transports.Count > 0)
            {
                DataBinding<UnitData>? chosenTransport = await PromptEmbarkChoice(deployingUnit, transports);

                if (chosenTransport != null)
                {
                    UnitData transport = chosenTransport.GetValue();
                    TransportUtilities.Embark(deployingUnit, transport);

                    context.Log($"{deployingUnit.Name} embarked {transport.Name} at deployment.");
                    await context.Announce($"{deployingUnit.Name} embarked {transport.Name}.",
                        new TextColor(255, 170, 60, 255), EBannerTier.Toast);

                    // Set aside, not placed: clear the current unit (as DeployUnitStage does after placing)
                    // and skip the placement stage, going back to choose the next player.
                    context.CurrentDeployingUnit = null;
                    await OnEmbarked.Activate(context);
                    return;
                }
            }

            // Deploy normally.
            await OnFinish.Activate(context);
        }

        /// <summary>
        /// Friendly transports already on the table that <paramref name="candidate"/> could embark — full
        /// eligibility (transport identity, same player, ride caps, remaining capacity) via
        /// <see cref="TransportUtilities.CanUnitEmbark"/>, restricted to deployed (on-battlefield) transports
        /// since you can only load a transport that is already down.
        /// </summary>
        private List<DataBinding<UnitData>> GetEligibleTransports(UnitData candidate)
        {
            List<DataBinding<UnitData>> allBindings = new List<DataBinding<UnitData>>();
            foreach (ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                allBindings.AddRange(army.UnitBindings);
            }

            List<IUnit> allUnits = allBindings.Select(binding => (IUnit)binding.GetValue()).ToList();

            List<DataBinding<UnitData>> eligible = new List<DataBinding<UnitData>>();
            foreach (DataBinding<UnitData> binding in allBindings)
            {
                UnitData unit = binding.GetValue();
                if (!TransportUtilities.IsTransport(unit, GameContext.RuleEvaluator)) continue;
                if (!unit.GetIsOnBattlefield()) continue;
                if (!TransportUtilities.CanUnitEmbark(candidate, unit, allUnits, GameContext.RuleEvaluator, out _)) continue;

                eligible.Add(binding);
            }

            return eligible;
        }

        /// <summary>
        /// Asks which eligible transport to load <paramref name="unit"/> into, or to deploy it normally.
        /// Cancel (a null reply) is a real choice here, not a back-out — there's no list to re-prompt — so
        /// <c>allowCancel</c> is true and null means "place this unit on the table instead". Because it is a
        /// choice rather than an escape hatch it names itself: <c>cancelLabel</c> makes the resolver's exit
        /// button read "Deploy Normally", after a playtester reported having to press Back to deploy a unit
        /// that was standing next to a transport (#335). The mid-game <c>EmbarkStage</c> prompt deliberately
        /// keeps the default "Back" — cancelling THAT one really does return to the action menu.
        /// </summary>
        private async Task<DataBinding<UnitData>?> PromptEmbarkChoice(UnitData unit,
            List<DataBinding<UnitData>> transports)
        {
            List<SelectionRequest<UnitData>.ValidOption> options = new List<SelectionRequest<UnitData>.ValidOption>();
            foreach (DataBinding<UnitData> transport in transports)
            {
                options.Add(new SelectionRequest<UnitData>.ValidOption(
                    transport, $"Embark into {transport.GetValue().Name}"));
            }

            SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(unit.PlayerID,
                $"Deploy {unit.Name} inside a transport, or on the table?",
                options, new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: true,
                displayName: $"Deploying {unit.Name}",
                cancelLabel: ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE);

            return await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);
        }
    }
}
