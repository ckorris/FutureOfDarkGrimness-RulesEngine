
using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class ChooseUnitToActivateStage : StageBase<IPlayerTurnContext>
    {
        public StageBinding ToMainUnitAction;
        public ChooseUnitToActivateStage(IGameContext gameContext, IStateMachineLayer<IPlayerTurnContext> parent)
            : base(gameContext, parent)
        {
            ToMainUnitAction = new StageBinding(this);
        }

        public override async Task Enter(IPlayerTurnContext context)
        {
            context.Log("Entered Choose Unit to Activate stage.");
    
            if(context.ActivatedPlayer == null)
            {
                throw new InvalidOperationException($"Entered {nameof(ChooseUnitToActivateStage)} while activated player was null.");
            }

            //Find all units.
            List<SelectionRequest<UnitData>.ValidOption> validOptions = new List<SelectionRequest<UnitData>.ValidOption>();
            List<SelectionRequest<UnitData>.InvalidOption> invalidOptions = new List<SelectionRequest<UnitData>.InvalidOption>();

            foreach (ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>()
                .Where(a => a.IsOwnedBy(context.ActivatedPlayer.Value)))
            {
                foreach(DataBinding<UnitData> potentialUnit in army.UnitBindings)
                {
                    if (potentialUnit.GetValue().HasActivatedThisTurn)
                    {
                        if (potentialUnit.GetValue().GetIsAlive())
                        {
                            validOptions.Add(new SelectionRequest<UnitData>.ValidOption(potentialUnit, potentialUnit.GetValue().Name));
                        }
                        
                        //If the unit is dead, don't bother listing it, the reason is obvious.
                    }
                    else
                    {
                        invalidOptions.Add(new SelectionRequest<UnitData>.InvalidOption(potentialUnit, potentialUnit.GetValue().Name,
                            "This unit has already activated."));
                    }
                }
            }

            //TODO: We don't catch if there are no options and we're stuck in the menu forever.
            SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(context.ActivatedPlayer.Value, "Choose Unit to Activate",
                validOptions, invalidOptions);

            DataBinding<UnitData> chosenUnit = await GameContext.PlayerRequester.RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>
                (context.ActivatedPlayer.Value, request);

            context.Log($"Activating: {chosenUnit.GetValue().Name}.");
            context.ChooseUnitToActivate(chosenUnit);
            ToMainUnitAction.Activate(context);
        }
    }
}