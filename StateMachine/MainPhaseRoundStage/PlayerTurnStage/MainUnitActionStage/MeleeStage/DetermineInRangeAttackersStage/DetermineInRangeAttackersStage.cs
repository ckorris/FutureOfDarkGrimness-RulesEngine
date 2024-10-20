
namespace FDG.Stages
{
    public class DetermineInRangeAttackersStage : StateBase<IMeleeContext>
    {
        public const string DETERMINE_IN_RANGE_ATTACKER_FINISHED_TRANSITION = "DetermineInRangeAttackerFinished";

        public DetermineInRangeAttackersStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
