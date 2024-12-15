using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FDG.Stages
{

    public class ChooseRangedWeaponStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnChoseWeapon;

        public ChooseRangedWeaponStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseWeapon = new StageBinding(this);
        }

        public override void Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedWeaponStage)}.");
            }

            //TODO: Instead of giving the entire list, need to make it changeable because of effects like Deadly.
            //That's why I'm making a second list, to display ones you can't choose yet.
            IReadOnlyDictionary<IWeapon, int> availableWeapons = new ConcurrentDictionary<IWeapon, int>(context.AvailableWeapons);
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons = new ConcurrentDictionary<IWeapon, int>();

            GameContext.GetHandler<IChooseRangedWeaponHandler>().Handle(availableWeapons, unavailableWeapons, (weapon) => ChooseWeapon(context, weapon));
        }

        private void ChooseWeapon(ICombatActionContext context, IWeapon chosenWeapon)
        {
            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            OnChoseWeapon.Activate(context);
        }
    }

    public interface IChooseRangedWeaponHandler
    {
        public void Handle(IReadOnlyDictionary<IWeapon, int> availableWeapons,
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons, Action<IWeapon> onChoseWeapon);
    }
}