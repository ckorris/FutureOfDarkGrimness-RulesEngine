
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
            GameContext.GetHandler<IChooseUnitToActivateHandler>().Handle(context, ToMainUnitAction.Activate);
        }
    }

    public interface IChooseUnitToActivateHandler : IExitOnlyHandler<IPlayerTurnContext>
    {

    }
}