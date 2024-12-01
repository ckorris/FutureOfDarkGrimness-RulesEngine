
namespace FDG.Stages
{

    public class DeterminePlayerTurnStage : StageBase<IPlayerTurnContext>
    {
        public const string DETERMINE_PLAYER_TO_CHOOSE_UNIT_TO_ACTIVATE_TRANSITION =
            "DeterminePlayerToChooseUnitToActivate";

        public DeterminePlayerTurnStage(StateMachine stateMachine, IPlayerTurnContext context, StageBase parentState = null) 
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