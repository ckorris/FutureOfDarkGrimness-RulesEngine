
namespace FDG.Stages
{

    public class ChooseUnitToActivateStage : StateBase<IPlayerTurnContext>
    {
        public const string CHOOSE_UNIT_TO_ACTIVATE_TO_MAIN_UNIT_ACTION_TRANSITION =
            "ChooseUnitToActivateToMainUnitAction";

        public ChooseUnitToActivateStage(StateMachine stateMachine, IPlayerTurnContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {

        }

        public override void Enter()
        {
            base.Enter();

            Context.ChooseUnitToActivateHandler.Handle(Context, MoveToMainUnitAction);
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