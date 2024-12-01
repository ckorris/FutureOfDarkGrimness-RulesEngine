
namespace FDG.Stages
{

    public class StartOfTurnExtraActionStage : StageBase<IMainPhaseContext>
    {
        public const string TO_DETERMINE_FIRST_TURN_TRANSITION = "StartExtraActionsToDetermineFirstTurn";

        public StartOfTurnExtraActionStage(StateMachine stateMachine, IMainPhaseContext context, StageBase parentState = null)
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