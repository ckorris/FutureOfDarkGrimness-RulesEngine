
namespace FDG.Stages
{

    public class DetermineFirstPlayerTurnStage : StateBase<IMainPhaseContext>
    {
        public const string DETERMINE_FIRST_PLAYER_TO_PLAYER_TURN_TRANSITION = "DetermineFirstPlayerToPlayerTurn";

        public DetermineFirstPlayerTurnStage(StateMachine stateMachine, IMainPhaseContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            //Temp as it has children.
            Context.TextOutput.Log("Skipping Determine First Player Turn for now.");
            SignalEvent(DETERMINE_FIRST_PLAYER_TO_PLAYER_TURN_TRANSITION);
        }
    }
}