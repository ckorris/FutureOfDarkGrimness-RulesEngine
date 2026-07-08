

using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public class ChooseUnitToDeployStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;

        // The chosen unit carries an Ambush-style rule and the player elected to hold it in reserve.
        // It counts as this player's deployment for the turn but is never placed, so we skip the
        // placement stages and go straight back to picking the next player.
        public StageBinding OnDeferred;

        private enum EAmbushChoice { Deploy, Hold, Back }

        public ChooseUnitToDeployStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
            OnDeferred = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.LogDebug("Entered Choose Unit to Deploy stage.");

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

            // Units a rule set aside for a later pass (Scout) can't be deployed now, but we still list them —
            // grayed out, with the rule name appended — so the player isn't confused about a "missing" unit.
            List<SelectionRequest<UnitData>.InvalidOption> deferredOptions =
                new List<SelectionRequest<UnitData>.InvalidOption>();
            foreach (DeferredUnitEntry deferred in context.DeferredUnits)
            {
                UnitData deferredUnit = deferred.Unit.GetValue();
                if (deferredUnit.PlayerID != currentPlayerID)
                {
                    continue;
                }

                string ruleName = GetDeferRuleName(deferredUnit);
                deferredOptions.Add(new SelectionRequest<UnitData>.InvalidOption(
                    deferred.Unit, $"{deferredUnit.Name} ({ruleName})", "deploys after normal deployment"));
            }

            // The player may pick an Ambush-capable unit, see the hold-or-deploy prompt, then back out
            // to choose a different unit — so we loop until they commit to deploying or holding one.
            // Nothing is removed from the pool until they commit, so backing out needs no undo.
            while (true)
            {
                //We don't account for things that can't deploy until later, but I can't think of a reason that ever happens
                //in GDF. For Scout, it's optional, and Aircraft deploys first.

                List<SelectionRequest<UnitData>.ValidOption> validOptions = new List<SelectionRequest<UnitData>.ValidOption>();

                // #029: Aircraft must deploy before all other units. While this player still has an undeployed
                // Aircraft, only its Aircraft are selectable; the rest are grayed out (alongside Scout deferrals)
                // until every Aircraft is placed.
                bool hasUndeployedAircraft = context.UndeployedUnits[currentPlayerID]
                    .Any(u => Rules.Dispatch.AircraftRules.IsAircraft(u.GetValue()));
                List<SelectionRequest<UnitData>.InvalidOption> invalidOptions =
                    new List<SelectionRequest<UnitData>.InvalidOption>(deferredOptions);

                foreach (DataBinding<UnitData> potentialUnit in context.UndeployedUnits[currentPlayerID])
                {
                    // Units that can be held in reserve are shown as "Name (RuleName)" so the player
                    // knows the hold-or-deploy choice is coming (e.g. "Infiltrators (Ambush)").
                    string label = potentialUnit.GetValue().Name;
                    if (TryGetReserveRuleName(potentialUnit.GetValue(), out string reserveRuleName))
                    {
                        label = $"{label} ({reserveRuleName})";
                    }

                    if (hasUndeployedAircraft && !Rules.Dispatch.AircraftRules.IsAircraft(potentialUnit.GetValue()))
                    {
                        invalidOptions.Add(new SelectionRequest<UnitData>.InvalidOption(potentialUnit, label,
                            "Aircraft deploy first"));
                        continue;
                    }

                    validOptions.Add(new SelectionRequest<UnitData>.ValidOption(potentialUnit, label));
                }

                // Choosing which unit to deploy is mandatory — no back-destination, so no cancel.
                SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(currentPlayerID, "Choose Unit to Deploy",
                    validOptions, invalidOptions, allowCancel: false);

                DataBinding<UnitData> chosenUnit =
                    await GameContext.PlayerRequester.RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>
                    (request);

                if (TryGetReserveRuleName(chosenUnit.GetValue(), out string ruleName))
                {
                    EAmbushChoice choice = await PromptHoldOrDeploy(chosenUnit.GetValue(), ruleName);

                    if (choice == EAmbushChoice.Back)
                    {
                        continue; // Re-prompt the unit list; nothing has been committed yet.
                    }

                    if (choice == EAmbushChoice.Hold)
                    {
                        context.UndeployedUnits[currentPlayerID].Remove(chosenUnit);
                        context.Log($"{chosenUnit.GetValue().Name} held in {ruleName}.");
                        await context.Announce($"{chosenUnit.GetValue().Name} held in {ruleName}.",
                            new TextColor(255, 170, 60, 255));
                        await OnDeferred.Activate(context);
                        return;
                    }

                    // EAmbushChoice.Deploy falls through to normal deployment below.
                }

                context.Log($"Activating {chosenUnit.GetValue().Name}.");

                context.CurrentDeployingUnit = chosenUnit;
                context.UndeployedUnits[currentPlayerID].Remove(chosenUnit);

                await OnFinish.Activate(context);
                return;
            }
        }

        // Stable option labels for the hold-or-deploy prompt, so resolvers (notably the AI) can match a
        // choice without depending on the dynamic prompt wording. The "hold" label is rule-name dependent
        // (HoldChoiceFor) since it reads naturally as "Hold in Ambush".
        public const string DEPLOY_NORMALLY_CHOICE = "Deploy normally";
        public const string BACK_TO_LIST_CHOICE = "Back to unit list";
        public static string HoldChoiceFor(string ruleName) => $"Hold in {ruleName}";

        // Asks whether to hold an Ambush-capable unit in reserve, deploy it normally, or back out to
        // re-pick. Uses the alias-aware rule display name so the prompt reads naturally.
        private async Task<EAmbushChoice> PromptHoldOrDeploy(IUnit unit, string ruleName)
        {
            string hold = HoldChoiceFor(ruleName);

            StringSelectionRequest request = new StringSelectionRequest(unit.PlayerID,
                $"Deploy {unit.Name} now, or hold it in {ruleName}?",
                new List<string> { hold, DEPLOY_NORMALLY_CHOICE, BACK_TO_LIST_CHOICE },
                new List<StringSelectionRequest.InvalidOption>());

            string result = await GameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            if (result == hold) return EAmbushChoice.Hold;
            if (result == BACK_TO_LIST_CHOICE) return EAmbushChoice.Back;
            return EAmbushChoice.Deploy;
        }

        // The alias-aware display name of whatever rule deferred this unit's deployment (Scout), for the
        // grayed-out list entry. The unit is already known to be deferred, so a defer op is expected.
        private string GetDeferRuleName(IUnit unit)
        {
            IReadOnlyList<(RuleOperation Op, string RuleName)> named = GameContext.RuleEvaluator
                .EvaluateAllNamed(new PreDeploymentSelectContext(unit), (unit, ERuleSeat.Actor));

            foreach ((RuleOperation op, string name) in named)
            {
                if (op is RuleOperation.DeferDeployment)
                {
                    return name;
                }
            }

            return "Reserve";
        }

        // True when the unit carries a rule that defers it to a later round (Ambush). Returns the
        // alias-aware display name of that rule for labelling and prompts.
        private bool TryGetReserveRuleName(IUnit unit, out string ruleName)
        {
            IReadOnlyList<(RuleOperation Op, string RuleName)> named = GameContext.RuleEvaluator
                .EvaluateAllNamed(new PreDeploymentSelectContext(unit), (unit, ERuleSeat.Actor));

            foreach ((RuleOperation op, string name) in named)
            {
                if (op is RuleOperation.DeferDeployment defer && defer.Timing == EDeferTiming.LaterRound)
                {
                    ruleName = name;
                    return true;
                }
            }

            ruleName = null!;
            return false;
        }
    }
}
