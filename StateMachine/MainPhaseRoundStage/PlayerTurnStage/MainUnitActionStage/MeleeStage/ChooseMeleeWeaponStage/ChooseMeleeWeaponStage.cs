using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class ChooseMeleeWeaponStage : StageBase<IMeleeContext>
    {
        public StageBinding OnChosen;

        public ChooseMeleeWeaponStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            OnChosen = new StageBinding(this);
        }

        public override void Enter(IMeleeContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedWeaponStage)}.");
            }

            //TODO: Instead of giving the entire list, need to make it changeable because of effects like Deadly.
            //That's why I'm making a second list, to display ones you can't choose yet.
            IReadOnlyDictionary<IWeapon, int> availableWeapons = new ConcurrentDictionary<IWeapon, int>(context.AvailableWeapons);
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons = new ConcurrentDictionary<IWeapon, int>();

            GameContext.GetHandler<IChooseMeleeWeaponHandler>().Handle(availableWeapons, unavailableWeapons, (weapon) => ChooseWeapon(context, weapon));
        }

        private void ChooseWeapon(IMeleeContext context, IWeapon chosenWeapon)
        {
            context.ChooseWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            OnChosen.Activate(context);
        }
    }

    public interface IChooseMeleeWeaponHandler
    {
        public void Handle(IReadOnlyDictionary<IWeapon, int> availableWeapons,
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons, Action<IWeapon> onChoseWeapon);
    }
}
