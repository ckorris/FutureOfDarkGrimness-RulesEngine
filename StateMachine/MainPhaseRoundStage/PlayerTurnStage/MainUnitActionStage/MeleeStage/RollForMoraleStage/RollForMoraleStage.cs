
namespace FDG.Stages
{
    public class RollForMoraleStage : StateBase<IMeleeContext>
    {
        public const string ROLL_FOR_MORALE_PASSED_TRANSITION = "RollForMoralePassed";
        public const string ROLL_FOR_MORALE_FAILED_TRANSITION = "RollForMoraleFailed";

        public RollForMoraleStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }
    }
}
