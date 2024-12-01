
namespace FDG.Stages
{

    public class ChooseUnitToActivateStage : StageBase<IPlayerTurnContext>
    {
        public const string CHOOSE_UNIT_TO_ACTIVATE_TO_MAIN_UNIT_ACTION_TRANSITION =
            "ChooseUnitToActivateToMainUnitAction";

        public ChooseUnitToActivateStage(StateMachine stateMachine, IPlayerTurnContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {

        }

        public override void Enter()
        {
            base.Enter();

            Context.GetHandler<IChooseUnitToActivateHandler>().Handle(Context, MoveToMainUnitAction);
        }


        private void MoveToMainUnitAction()
        {
            SignalEvent(CHOOSE_UNIT_TO_ACTIVATE_TO_MAIN_UNIT_ACTION_TRANSITION);
        }
    }

    public interface IChooseUnitToActivateHandler : IExitOnlyHandler<IPlayerTurnContext>
    {

    }
}