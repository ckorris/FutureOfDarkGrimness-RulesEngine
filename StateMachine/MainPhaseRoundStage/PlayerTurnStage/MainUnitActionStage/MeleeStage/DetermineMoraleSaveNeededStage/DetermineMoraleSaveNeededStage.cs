
namespace FDG.Stages
{
    public class DetermineMoraleSaveNeededStage : StateBase<IMeleeContext>
    {
        public const string DETERMINE_MORALE_SAVE_NEEDED_FINISHED_TRANSITION = "DetermineMoraleSaveNeededFinished";

        public DetermineMoraleSaveNeededStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
