
namespace FDG.Stages
{

    public class DetermineFirstPlayerTurnStage : StageBase<IMainPhaseContext>
    {
        public const string DETERMINE_FIRST_PLAYER_TO_PLAYER_TURN_TRANSITION = "DetermineFirstPlayerToPlayerTurn";

        public DetermineFirstPlayerTurnStage(StateMachine stateMachine, IMainPhaseContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            //Temp as it has children.
            Context.Log("Skipping Determine First Player Turn for now.");
            SignalEvent(DETERMINE_FIRST_PLAYER_TO_PLAYER_TURN_TRANSITION);
        }
    }
}