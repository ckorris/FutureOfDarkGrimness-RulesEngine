
namespace FDG.Stages
{
    public class ChooseMeleeWeaponStage : StateBase<IMeleeContext>
    {
        public const string CHOOSE_MELEE_WEAPON_FINISHED_TRANSITION = "ChooseMeleeWeaponFinished";

        public ChooseMeleeWeaponStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }
    }
}
