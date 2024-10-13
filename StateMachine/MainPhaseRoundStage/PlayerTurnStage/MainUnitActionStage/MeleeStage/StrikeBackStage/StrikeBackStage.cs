
namespace FDG.Stages
{

    public class StrikeBackStage : StateBase<IMeleeContext>
    {
        public const string STRIKE_BACK_TO_RESOLVE_MELEE_MORALE_TRANSITION =
            "StrikeBackToResolveMeleeMoraleTransition";

        public StrikeBackStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Entered Strike Back stage. Striking back. (Moving on for now.)");
            MoveToResolveMeleeMorale();
        }

        private void MoveToResolveMeleeMorale()
        {
            SignalEvent(STRIKE_BACK_TO_RESOLVE_MELEE_MORALE_TRANSITION);
        }
    }
}