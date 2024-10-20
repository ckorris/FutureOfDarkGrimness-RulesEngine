
namespace FDG.Stages
{

    public class StrikeBackStage : StateBase<IMeleeContext>
    {
        private const string STRIKE_BACK_TO_CHILD_ENTRANCE_TRANSITION =
            "StrikeBackToChildEntrance";

        public StrikeBackStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public void AssignNormalExitStage(StateBase targetStageWhenFinished)
        {
            throw new NotImplementedException();
        }

        public void AssignAttackerKilledExitStage(StateBase targetStageWhenAttackerKilled)
        {
            throw new NotImplementedException();
        }

        public override void Enter()
        {
            base.Enter();

            throw new NotImplementedException();

            MoveToChildBuildTargetListStage();
        }

        private void MoveToChildBuildTargetListStage()
        {
            SignalEvent(STRIKE_BACK_TO_CHILD_ENTRANCE_TRANSITION);
        }
    }
}