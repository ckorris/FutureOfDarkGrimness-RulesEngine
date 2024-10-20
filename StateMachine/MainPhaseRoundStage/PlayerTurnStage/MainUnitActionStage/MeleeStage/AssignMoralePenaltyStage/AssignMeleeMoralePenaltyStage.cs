
namespace FDG.Stages
{
    public class AssignMeleeMoralePenaltyStage : StateBase<IMeleeContext>
    {
        public const string ASSIGN_MELEE_MORALE_PENALTY_FINISHED_TRANSITION = "AssignMeleeMoralePenaltyFinished";

        public AssignMeleeMoralePenaltyStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
