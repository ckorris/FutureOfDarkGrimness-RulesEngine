
namespace FDG.Stages
{
    public class DetermineInRangeAttackersStage : StateBase<IMeleeContext>
    {
        public const string DETERMINE_IN_RANGE_ATTACKER_FINISHED_TRANSITION = "DetermineInRangeAttackerFinished";

        private const float HORIZONTAL_ATTACK_RANGE_INCHES = 2;
        private const float VERTICAL_ATTACK_RANGE_INCHES = 4;

        public DetermineInRangeAttackersStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Entering Determine In Range Attackers. Skipping, for now we let everyone fight.");
            MoveToNextStage();
        }

        private void MoveToNextStage()
        {
            SignalEvent(DETERMINE_IN_RANGE_ATTACKER_FINISHED_TRANSITION);
        }
    }
}
