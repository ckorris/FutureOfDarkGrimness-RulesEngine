
namespace FDG.Stages
{
    public class DetermineInRangeDefendersStage : StateBase<IMeleeContext>
    {
        public const string DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION = "DetermineInRangeDefenderFinished";

        public DetermineInRangeDefendersStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
