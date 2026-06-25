using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class DeterminePlayerTurnStage : StageBase<ISingleRoundContext>
    {
        public StageBinding OnDeterminedPlayerTurn;
        public StageBinding OnNoPlayersLeft;

        // The player whose "<name>'s Activation" banner we last showed, so we don't repeat it when the
        // same player activates twice in a row (e.g. a Martial Prowess reactivation, or an opponent with
        // nothing left to activate).
        private PlayerID? _lastAnnouncedPlayer;

        public DeterminePlayerTurnStage(IGameContext gameContext, IStateMachineLayer<ISingleRoundContext> parent) : base(gameContext, parent)
        {
            OnDeterminedPlayerTurn = new StageBinding(this);
            OnNoPlayersLeft = new StageBinding(this);
        }

        public override async Task Enter(ISingleRoundContext context)
        {
            //NOTE: See DetermineNextDeployPlayerStage for code that iterates through teams and players to see who should deploy next.
            //Might be able to move that code to a utility somehow, though differences exist.

            context.Log("Entering Determine Next Player Turn stage.");

            // Rolling save point (#052): snapshot the flow state at the start of each activation
            // cycle, before the next unit is chosen and before it is marked activated. A load taken
            // at any moment resumes from the most recent snapshot, re-playing the activation that was
            // in progress. Guarded so minimal-store unit tests (no GameProgressData type) are unaffected.
            if (GameContext.GameDataStore.IsTypeAssigned<GameProgressData>())
            {
                GameProgressUtilities.WriteProgress(
                    GameContext.GameDataStore,
                    GameProgressUtilities.Capture(context, GameContext.Settings, EResumeStage.MainPhase));
            }

            if(context.TryAdvanceToNextPlayer(out ITeam? nextTeam, out PlayerID? nextPlayerID) == false)
            {
                context.Log("No players left to activate. Ending round.");
                await OnNoPlayersLeft.Activate(context);
                return;
            }

            if (_lastAnnouncedPlayer != nextPlayerID.Value)
            {
                _lastAnnouncedPlayer = nextPlayerID.Value;
                await context.Announce($"{context.GetPlayerName(nextPlayerID.Value)}'s Activation",
                    new TextColor(130, 220, 130, 255));
            }

            // #042 Martial Prowess: before the active player picks their next unit, offer a second
            // activation to any of their already-activated units bearing the rule. Accepting re-adds the
            // unit to the round's unactivated pool so it appears as a choice in ChooseUnitToActivateStage.
            await OfferReactivations(context, nextPlayerID!.Value);

            await OnDeterminedPlayerTurn.Activate(context);
        }

        /// <summary>
        /// Fires <see cref="Rules.Foundation.EHookID.Activation_OnNextActivatorRequested"/> for each of
        /// the active player's already-activated, on-table units and offers any activated ability gathered
        /// there (Martial Prowess's reactivation). The reactivate operation is a marker the stage applies
        /// directly — re-adding the unit to the pool — because the round's activation state isn't reachable
        /// through the <c>IOperationServices</c> seam (mirrors how DeferDeployment is applied stage-side).
        /// The once-per-game cost marker is granted here too, since the imperative-op executor only runs
        /// ExecutableOperations and never applies cost tokens.
        /// </summary>
        private async Task OfferReactivations(ISingleRoundContext context, PlayerID playerID)
        {
            // Candidates: living, on-table units of this player that have already activated this round
            // (i.e. are no longer in the unactivated pool). Snapshot up front — accepting an offer mutates
            // the pool.
            List<DataBinding<UnitData>> alreadyActivated = GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(army => army.IsOwnedBy(playerID))
                .SelectMany(army => army.UnitBindings)
                .Where(unit => unit.GetValue().GetIsAlive()
                    && unit.GetValue().GetIsOnBattlefield()
                    && context.UnactivatedUnits[playerID].Contains(unit) == false)
                .ToList();

            foreach (DataBinding<UnitData> unit in alreadyActivated)
            {
                var hookContext = new NextActivatorRequestedContext(unit.GetValue());

                foreach (AbilityOffer offer in GameContext.RuleEvaluator.GatherOffers(hookContext))
                {
                    var question = new YesNoRequest(playerID,
                        $"Reactivate {unit.GetValue().Name} with {offer.RuleName}?",
                        defaultAnswer: true);
                    bool accepted = await GameContext.PlayerRequester
                        .RequestDecision<YesNoRequest, bool>(question);

                    if (!accepted) continue;

                    GameContext.Log($"{unit.GetValue().Name} used {offer.RuleName} to activate again.");

                    IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator
                        .ResolveAbility(offer, new[] { (IUnit)unit.GetValue() });

                    ApplyReactivationOps(context, unit, ops);
                }
            }
        }

        /// <summary>
        /// Applies the operation queue produced by an accepted reactivation: the shared token applier grants
        /// the cost marker the once-per-game gate reads, and the <see cref="RuleOperation.InvokeReactivate"/>
        /// marker re-adds the unit to the unactivated pool.
        /// </summary>
        private static void ApplyReactivationOps(ISingleRoundContext context, DataBinding<UnitData> unit,
            IReadOnlyList<RuleOperation> ops)
        {
            OperationApplier.ApplyTokenOperations(ops);

            if (ops.OfType<RuleOperation.InvokeReactivate>().Any())
            {
                context.ReinstateUnitForActivation(unit);
            }
        }
    }
}
