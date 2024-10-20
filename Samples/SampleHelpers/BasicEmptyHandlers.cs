

using FDG.Stages;

namespace FDG.Samples
{
    public class BasicTesterChooseActionHandler : IChooseActionHandler
    {
        public void Handle(IUnitActionContext context, Action chooseMovement, Action pass)
        {
            //For tests, we choose movement, because at least for now, that's how you attack - 
            //you choose your move and your attack at basically the same time, as how you move
            //affects your attack options.
            chooseMovement();
        }
    }

    public class BasicTesterMovementHandler : IMovementHandler
    {
        public void Handle(IUnitActionContext actionContext, Action onChooseMelee, Action onChooseRanged, Action onChooseNonCombat)
        {
            onChooseRanged();
        }
    }

    public class BasicTesterOfferStrikeBackHandler : IOfferStrikeBackHandler
    {
        private readonly bool _strikeBack;
        public BasicTesterOfferStrikeBackHandler(bool strikeBack)
        {
            _strikeBack = strikeBack;
        }

        public void Handle(IMeleeContext context, Action acceptStrikeBack, Action rejectStrikeBack)
        {
            if(_strikeBack)
            {
                acceptStrikeBack();
            }
            else
            {
                rejectStrikeBack();
            }
        }
    }

    public class BasicTesterChooseWeaponHandler : IChooseMeleeWeaponHandler, IChooseRangedWeaponHandler
    {
        public void Handle(IReadOnlyDictionary<IWeapon, int> availableWeapons, IReadOnlyDictionary<IWeapon, int> unavailableWeapons,
            Action<IWeapon> onChoseWeapon)
        {
            //Just choose the next weapon automatically.
            IWeapon firstWeapon = availableWeapons.First().Key;
            onChoseWeapon(firstWeapon);
        }
    }

    public class BasicTesterChooseRangedTargetHandler : IChooseRangedTargetHandler
    {
        public void Handle(IReadOnlyList<IUnit> potentialTargetUnits, Action<IUnit> onChoseUnit)
        {
            //Just choose the first.
            IUnit firstUnit = potentialTargetUnits.First();
            onChoseUnit(firstUnit);
        }
    }

    public class BasicTesterAssignWoundsHandler : IAssignWoundsHandler
    {
        public void Handle(IUnit defendingUnit, AssignWoundsResults woundsResults, Action onWoundsAssigned)
        {
            woundsResults.AutoFill();
            onWoundsAssigned();
        }
    }
}
