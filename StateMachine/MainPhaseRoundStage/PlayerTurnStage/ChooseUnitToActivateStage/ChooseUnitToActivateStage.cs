
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

        public override void Enter(IPlayerTurnContext context)
        {
            context.Log($"Entered {nameof(ChooseUnitToActivateStage)}.");

            List<ActionChoice> unitChoices = new List<ActionChoice>();

            //Find all units.
            //In the future, just do ones that the player owns, and that haven't yet activated.
            bool canActivate = true; //Temp.
            foreach(IArmy army in GameContext.TableState.Armies.Objects)
            {
                foreach (IUnit unit in army.Units)
                { 
                    ActionChoice choice = new ActionChoice(() => OnChoseUnit(context, unit), unit.Name, canActivate, "");
                    unitChoices.Add(choice);
                }
            }


            GameContext.GetHandler<IChooseUnitToActivateHandler>().Handle(context, unitChoices);
        }

        private void OnChoseUnit(IPlayerTurnContext context, IUnit chosenUnit)
        {
            context.Log($"Activating: {chosenUnit.Name}.");
            context.ChooseUnitToActivate(chosenUnit);
            ToMainUnitAction.Activate(context);
        }
    }

    public interface IChooseUnitToActivateHandler
    {
        public void Handle(IPlayerTurnContext context, List<ActionChoice> unitChoices);
    }
}