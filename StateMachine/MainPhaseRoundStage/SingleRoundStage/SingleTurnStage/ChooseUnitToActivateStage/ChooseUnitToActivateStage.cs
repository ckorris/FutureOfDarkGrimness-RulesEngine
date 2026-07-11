
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class ChooseUnitToActivateStage : StageBase<ISingleTurnContext>
    {
        public StageBinding ToMainUnitAction;

        // #197 Delayed Action: the acting player chose to hold the picked unit back (pass the turn). Routes
        // straight to end-of-turn, skipping activation - the unit stays in the pool for a later turn.
        public StageBinding ToDelayedTurnEnd;

        public ChooseUnitToActivateStage(IGameContext gameContext, IStateMachineLayer<ISingleTurnContext> parent)
            : base(gameContext, parent)
        {
            ToMainUnitAction = new StageBinding(this);
            ToDelayedTurnEnd = new StageBinding(this);
        }

        public override async Task Enter(ISingleTurnContext context)
        {
            context.LogDebug("Entered Choose Unit to Activate stage.");
    
            //Find all units.
            List<SelectionRequest<UnitData>.ValidOption> validOptions = new List<SelectionRequest<UnitData>.ValidOption>();
            List<SelectionRequest<UnitData>.InvalidOption> invalidOptions = new List<SelectionRequest<UnitData>.InvalidOption>();

            foreach (ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(a => a.IsOwnedBy(context.ActivatedPlayer)))
            {
                foreach(DataBinding<UnitData> potentialUnit in army.UnitBindings)
                {
                    if (potentialUnit.GetValue().GetIsDead())
                    {
                        //If the unit is dead, don't bother listing it, the reason is obvious.
                        continue;
                    }

                    if (context.PlayerUnactivatedUnits.Contains(potentialUnit))
                    {
                        validOptions.Add(new SelectionRequest<UnitData>.ValidOption(potentialUnit, potentialUnit.GetValue().Name));
                    }
                    else
                    {
                        invalidOptions.Add(new SelectionRequest<UnitData>.InvalidOption(potentialUnit, potentialUnit.GetValue().Name,
                            GetUnavailableReason(potentialUnit.GetValue())));
                    }
                }
            }

            //TODO: We don't catch if there are no options and we're stuck in the menu forever.
            // Choosing which unit to activate is mandatory — no back-destination, so no cancel (a null/Back
            // reply has nowhere to go and crashes the networked reply path).
            // #191 A4-1: its own request TYPE (not a bare SelectionRequest<UnitData>) so AI resolvers
            // replace exactly this decision without string-matching the prompt.
            ChooseUnitToActivateRequest request = new ChooseUnitToActivateRequest(
                context.ActivatedPlayer, validOptions, invalidOptions);

            System.Diagnostics.Debug.WriteLine($"Choose unit requesting player {context.ActivatedPlayer}. ");

            DataBinding<UnitData> chosenUnit = await GameContext.PlayerRequester
                .RequestDecision<ChooseUnitToActivateRequest, DataBinding<UnitData>>(request);

            // #197 Delayed Action: if the chosen unit can hold back this turn, offer it before committing to
            // the activation. Accepting passes the turn (the unit stays in the pool) rather than activating.
            if (CanOfferDelayedAction(context, chosenUnit.GetValue()))
            {
                var holdBack = new YesNoRequest(context.ActivatedPlayer,
                    $"Delayed Action: hold {chosenUnit.GetValue().Name} back to activate later this round " +
                    "(your opponent activates next instead)?",
                    defaultAnswer: false);

                bool delay = await GameContext.PlayerRequester
                    .RequestDecision<YesNoRequest, bool>(holdBack);

                if (delay)
                {
                    // One hold-back per team per round: mark the unit so the team-wide scan blocks another.
                    chosenUnit.GetValue().Tokens.AddToken(
                        TokenDefinitionCatalog.Create(TokenType.DelayedActionUsed));
                    context.MarkTurnDelayed();
                    context.Log($"{chosenUnit.GetValue().Name} used Delayed Action to hold back - " +
                        "the opponent activates next.");
                    await ToDelayedTurnEnd.Activate(context);
                    return;
                }
            }

            context.Log($"Activating: {chosenUnit.GetValue().Name}.");
            context.ChooseUnitToActivate(chosenUnit);
            await ToMainUnitAction.Activate(context);
        }

        /// <summary>
        /// Delayed Action ("once per round, if your opponent has more units left to activate than you, this
        /// unit may pass its turn instead of activating"): offer the hold-back iff the chosen unit carries the
        /// rule, an opposing team has more units left to activate (snapshotted on the turn context), and this
        /// team has not already used its once-per-round hold-back - detected by scanning the team's living
        /// units for the <see cref="TokenType.DelayedActionUsed"/> marker.
        /// </summary>
        private bool CanOfferDelayedAction(ISingleTurnContext context, UnitData chosenUnit)
        {
            if (!context.OpponentHasMoreUnitsToActivate) return false;
            if (!UnitHasDelayedAction(chosenUnit)) return false;
            if (TeamAlreadyDelayedThisRound(context.ActivatedPlayer)) return false;
            return true;
        }

        private static bool UnitHasDelayedAction(UnitData unit)
        {
            bool Has(IReadOnlyList<ResolvedRule> rules) =>
                rules.Any(r => r.Definition.Name == CoreRuleCatalog.DelayedActionRuleName);

            if (Has(unit.RuleDefinitions)) return true;
            // Defensive: the rule is unit-scoped, but a per-model attach (joined hero) would live on a model.
            return unit.Models.Any(m => m is ModelData md && Has(md.RuleDefinitions));
        }

        private bool TeamAlreadyDelayedThisRound(PlayerID actingPlayer)
        {
            ITeam actingTeam = GameContext.TableState.Teams.Objects
                .First(team => team.IsPlayerOnTeam(actingPlayer));

            return GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(army => actingTeam.Players.Contains(army.PlayerID))
                .SelectMany(army => army.UnitBindings)
                .Where(unit => unit.GetValue().GetIsAlive())
                .Any(unit => unit.GetValue().Tokens.HasToken(TokenType.DelayedActionUsed));
        }

        // Why a unit can't be activated right now. An unplaced Ambush reserve (off-table, deferred to a
        // later round) is not "already activated" — surface its real status, including the round-1 rule
        // that reserves can't arrive until round 2. (StartOfRoundExtraActionStage uses the same two
        // checks to decide who it offers to bring on.)
        private string GetUnavailableReason(UnitData unit)
        {
            if (Rules.Dispatch.ReserveRules.IsInReserve(unit) && TryGetLaterRoundDefer(unit, out _))
            {
                int round = GameProgressUtilities.TryGetProgress(GameContext.GameDataStore)?.RoundCount ?? 1;
                return round < 2
                    ? "Reserve - arrives round 2."
                    : "In Ambush reserve.";
            }

            return "Already activated.";
        }

        private bool TryGetLaterRoundDefer(IUnit unit, out RuleOperation.DeferDeployment defer)
        {
            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.Evaluate(
                unit, ERuleSeat.Actor, new PreDeploymentSelectContext(unit));

            defer = ops.OfType<RuleOperation.DeferDeployment>()
                .FirstOrDefault(d => d.Timing == EDeferTiming.LaterRound);
            return defer != null;
        }
    }
}