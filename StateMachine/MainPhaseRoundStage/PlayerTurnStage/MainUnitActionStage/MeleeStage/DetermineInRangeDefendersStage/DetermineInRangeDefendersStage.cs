
namespace FDG.Stages
{
    public class DetermineInRangeDefendersStage : StageBase<IMeleeContext>
    {
        public const string DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION = "DetermineInRangeDefenderFinished";

        public DetermineInRangeDefendersStage(StateMachine stateMachine, IMeleeContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Entering Determine In Range Defenders. Skipping, for now we let everyone fight.");
            MoveToNextStage();
        }

        private void MoveToNextStage()
        {
            SignalEvent(DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION);
        }
    }
}
