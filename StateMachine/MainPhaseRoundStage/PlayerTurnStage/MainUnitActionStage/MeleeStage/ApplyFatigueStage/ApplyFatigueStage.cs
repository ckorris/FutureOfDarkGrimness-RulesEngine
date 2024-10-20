
namespace FDG.Stages
{
    public class ApplyFatigueStage : StateBase<IMeleeContext>
    {
        public const string APPLY_FATIGUE_FINISHED_TRANSITION = "ApplyFatiqueFinished";

        public ApplyFatigueStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
