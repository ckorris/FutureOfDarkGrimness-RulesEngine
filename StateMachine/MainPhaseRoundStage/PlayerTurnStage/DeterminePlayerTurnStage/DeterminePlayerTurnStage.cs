
namespace FDG.StateMachine
{

    public class DeterminePlayerTurnStage : StateBase<IPlayerTurnContext>
    {
        public const string DETERMINE_PLAYER_TO_CHOOSE_UNIT_TO_ACTIVATE_TRANSITION =
            "DeterminePlayerToChooseUnitToActivate";

        public DeterminePlayerTurnStage(StateMachine stateMachine, IPlayerTurnContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Determine Player Turn Stage entered and moving through.");
            MoveToChooseUnitToActivate();
        }

        private void MoveToChooseUnitToActivate()
        {
            SignalEvent(DETERMINE_PLAYER_TO_CHOOSE_UNIT_TO_ACTIVATE_TRANSITION);
        }
    }
}