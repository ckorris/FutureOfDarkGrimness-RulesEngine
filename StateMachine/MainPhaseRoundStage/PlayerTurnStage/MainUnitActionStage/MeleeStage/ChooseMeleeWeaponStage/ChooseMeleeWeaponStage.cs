
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

    public interface IChooseMeleeWeaponHandler
    {
        public void Handle(IReadOnlyDictionary<IWeapon, int> availableWeapons,
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons, Action<IWeapon> onChoseWeapon);
    }
}
