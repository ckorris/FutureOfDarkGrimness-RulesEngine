
using FDG.Data;
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
                            "This unit has already activated."));
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
    }
}