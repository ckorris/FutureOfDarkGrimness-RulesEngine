
namespace FDG.Stages
{
    public class ApplyFatigueStage : StageBase<IMeleeContext>
    {
        public const string APPLY_FATIGUE_FINISHED_TRANSITION = "ApplyFatiqueFinished";

        public ApplyFatigueStage(StateMachine stateMachine, IMeleeContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
