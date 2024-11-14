
namespace FDG.Stages
{

    public class StartOfTurnExtraActionStage : StateBase<IMainPhaseContext>
    {
        public const string TO_DETERMINE_FIRST_TURN_TRANSITION = "StartExtraActionsToDetermineFirstTurn";

        public StartOfTurnExtraActionStage(StateMachine stateMachine, IMainPhaseContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            Context.GetHandler<IStartOfTurnExtraActionsHandler>().Handle(Context, MoveToDetermineFirstTurn);
        }

        private void MoveToDetermineFirstTurn()
        {
            SignalEvent(TO_DETERMINE_FIRST_TURN_TRANSITION);
        }
    }

    public interface IStartOfTurnExtraActionsHandler : IExitOnlyHandler<IMainPhaseContext>
    {

    }
}