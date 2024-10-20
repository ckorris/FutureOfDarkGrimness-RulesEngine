
namespace FDG.Stages
{
    public class DetermineMeleeWinnerStage : StateBase<IMeleeContext>
    {
        public const string DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION = "DetermineMeleeWinnerNeedsRoll";
        public const string DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION = "DetermineMeleeWinnerDoesntNeedRoll";

        public DetermineMeleeWinnerStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
