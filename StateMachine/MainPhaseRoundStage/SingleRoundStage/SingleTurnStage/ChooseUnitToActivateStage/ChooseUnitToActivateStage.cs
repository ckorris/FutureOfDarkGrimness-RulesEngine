
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class ChooseUnitToActivateStage : StageBase<ISingleTurnContext>
    {
        public StageBinding ToMainUnitAction;
        public ChooseUnitToActivateStage(IGameContext gameContext, IStateMachineLayer<ISingleTurnContext> parent)
            : base(gameContext, parent)
        {
            ToMainUnitAction = new StageBinding(this);
        }

        public override async Task Enter(ISingleTurnContext context)
        {
            context.Log("Entered Choose Unit to Activate stage.");
    
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
            SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(context.ActivatedPlayer, "Choose Unit to Activate",
                validOptions, invalidOptions, allowCancel: false);

            System.Diagnostics.Debug.WriteLine($"Choose unit requesting player {context.ActivatedPlayer}. ");

            DataBinding<UnitData> chosenUnit = await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);

            context.Log($"Activating: {chosenUnit.GetValue().Name}.");
            context.ChooseUnitToActivate(chosenUnit);
            await ToMainUnitAction.Activate(context);
        }

        // Why a unit can't be activated right now. An unplaced Ambush reserve (off-table, deferred to a
        // later round) is not "already activated" — surface its real status, including the round-1 rule
        // that reserves can't arrive until round 2. (StartOfRoundExtraActionStage uses the same two
        // checks to decide who it offers to bring on.)
        private string GetUnavailableReason(UnitData unit)
        {
            if (IsUnplaced(unit) && TryGetLaterRoundDefer(unit, out _))
            {
                int round = GameProgressUtilities.TryGetProgress(GameContext.GameDataStore)?.RoundCount ?? 1;
                return round < 2
                    ? "Ambush reserves can't arrive until round 2."
                    : "In Ambush reserve (not yet deployed).";
            }

            return "This unit has already activated.";
        }

        // A unit that has never been placed has all models at the default origin (0,0,0).
        private static bool IsUnplaced(UnitData unit)
        {
            foreach (DataBinding<ModelData> model in unit.ModelBindings)
            {
                Position pos = model.GetValue().PositionBinding.GetValue();
                if (pos.x != 0f || pos.z != 0f) return false;
            }
            return true;
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