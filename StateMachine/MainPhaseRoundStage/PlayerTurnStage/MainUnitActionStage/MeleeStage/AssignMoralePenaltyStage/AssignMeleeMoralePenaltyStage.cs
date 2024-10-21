
namespace FDG.Stages
{
    public class AssignMeleeMoralePenaltyStage : StateBase<IMeleeContext>
    {
        public const string ASSIGN_MELEE_MORALE_PENALTY_FINISHED_TRANSITION = "AssignMeleeMoralePenaltyFinished";

        public AssignMeleeMoralePenaltyStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            //TODO: Finish once we have a way to fatigue a unit.
            Context.Log("Assigning melee morale penalty. (Not actually for now)");
            MoveToNextStage();
        }

        private void MoveToNextStage()
        {
            SignalEvent(ASSIGN_MELEE_MORALE_PENALTY_FINISHED_TRANSITION);
        }
    }
}
