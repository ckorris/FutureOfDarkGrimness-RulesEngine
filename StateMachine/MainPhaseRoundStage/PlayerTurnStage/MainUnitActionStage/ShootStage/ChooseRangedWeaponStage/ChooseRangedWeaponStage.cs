using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FDG.Stages
{

    public class ChooseRangedWeaponStage : StateBase<IRangedContext>
    {
        public const string CHOOSE_RANGED_WEAPON_TO_CHOOSE_RANGED_TARGET_TRANSITION =
            "ChooseRangedWeaponToChooseRangedTarget";

        public ChooseRangedWeaponStage(StateMachine stateMachine, IRangedContext context, StateBase parentState = null)
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

            Context.GetHandler<IChooseRangedWeaponHandler>().Handle(availableWeapons, unavailableWeapons, ChooseWeapon);
        }

        private void ChooseWeapon(IWeapon chosenWeapon)
        {
            Context.ChooseWeapon(chosenWeapon, out int weaponCount);
            Context.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            SignalEvent(CHOOSE_RANGED_WEAPON_TO_CHOOSE_RANGED_TARGET_TRANSITION);
        }
    }

    public interface IChooseRangedWeaponHandler
    {
        public void Handle(IReadOnlyDictionary<IWeapon, int> availableWeapons,
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons, Action<IWeapon> onChoseWeapon);
    }
}