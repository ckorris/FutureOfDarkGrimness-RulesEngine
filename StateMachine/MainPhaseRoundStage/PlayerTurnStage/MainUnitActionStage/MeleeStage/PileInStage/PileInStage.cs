
namespace FDG.Stages
{
    public class PileInStage : StateBase<IMeleeContext>
    {
        public const string PINE_IN_FINISHED_TRANSITION = "PileInFinished";

        public PileInStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
