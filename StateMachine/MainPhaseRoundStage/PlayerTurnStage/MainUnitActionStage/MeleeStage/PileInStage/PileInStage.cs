
namespace FDG.Stages
{
    public class PileInStage : StateBase<IMeleeContext>
    {
        public const string PILE_IN_FINISHED_TRANSITION = "PileInFinished";

        public PileInStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {

        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Entered pile in stage. Skipping for now.");
            MoveToNextStage();

        }

        private void MoveToNextStage()
        {
            SignalEvent(PILE_IN_FINISHED_TRANSITION);
        }
    }
}
