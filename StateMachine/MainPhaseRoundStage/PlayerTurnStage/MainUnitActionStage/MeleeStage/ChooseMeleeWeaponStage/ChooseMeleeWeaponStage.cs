using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class ChooseMeleeWeaponStage : StateBase<IMeleeContext>
    {
        public const string CHOOSE_MELEE_WEAPON_FINISHED_TRANSITION = "ChooseMeleeWeaponFinished";

        public ChooseMeleeWeaponStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            if (Context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedWeaponStage)}.");
            }

            //TODO: Instead of giving the entire list, need to make it changeable because of effects like Deadly.
            //That's why I'm making a second list, to display ones you can't choose yet.
            IReadOnlyDictionary<IWeapon, int> availableWeapons = new ConcurrentDictionary<IWeapon, int>(Context.AvailableWeapons);
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons = new ConcurrentDictionary<IWeapon, int>();

            Context.GetHandler<IChooseMeleeWeaponHandler>().Handle(availableWeapons, unavailableWeapons, ChooseWeapon);
        }

        private void ChooseWeapon(IWeapon chosenWeapon)
        {
            Context.ChooseWeapon(chosenWeapon, out int weaponCount);
            Context.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            SignalEvent(CHOOSE_MELEE_WEAPON_FINISHED_TRANSITION);
        }
    }

    public interface IChooseMeleeWeaponHandler
    {
        public void Handle(IReadOnlyDictionary<IWeapon, int> availableWeapons,
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons, Action<IWeapon> onChoseWeapon);
    }
}
