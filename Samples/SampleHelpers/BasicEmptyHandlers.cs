using FDG.Stages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Samples
{
    /// <summary>
    /// Will perform only the action listed by <see cref="TestActionChoice"/>, then will pass after the first time.
    /// </summary>
    public class BasicTesterChooseActionHandler : IChooseActionHandler
    {
        public ETestActionChoice TestActionChoice;

        public BasicTesterChooseActionHandler(ETestActionChoice choice)
        {
            TestActionChoice = choice;
        }



        public void Handle(IUnitActionContext context, List<IChooseActionHandler.ActionChoice> actionChoices, Action onPass)
        {
            switch (TestActionChoice)
            {
                case ETestActionChoice.Movement:
                    IChooseActionHandler.ActionChoice moveChoice = actionChoices.First(choice => choice.ChoiceName == ChooseActionStage.MOVEMENT_CHOICE_NAME);
                    ActivateOrPassIfCant(context, moveChoice, onPass);
                    break;
                case ETestActionChoice.Melee:
                    IChooseActionHandler.ActionChoice chargeChoice = actionChoices.First(choice => choice.ChoiceName == ChooseActionStage.CHARGE_CHOICE_NAME);
                    ActivateOrPassIfCant(context, chargeChoice, onPass);
                    break;
                case ETestActionChoice.Ranged:
                    IChooseActionHandler.ActionChoice shootChoice = actionChoices.First(choice => choice.ChoiceName == ChooseActionStage.SHOOT_CHOICE_NAME);
                    ActivateOrPassIfCant(context, shootChoice, onPass);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ActivateOrPassIfCant(IUnitActionContext context, IChooseActionHandler.ActionChoice actionChoice, Action onPass)
        {
            if (actionChoice.CanActivate)
            {
                actionChoice.Choose();
            }
            else
            {
                context.Log($"Couldn't choose {actionChoice.ChoiceName}. Reason: {actionChoice.ReasonCannotActivate}. Finishing activation.");
                onPass();
            }
        }

        public enum ETestActionChoice
        {
            Movement,
            Melee,
            Ranged
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
        public  bool StrikeBack;
        public BasicTesterOfferStrikeBackHandler(bool strikeBack)
        {
            StrikeBack = strikeBack;
        }

        public void Handle(IMeleeContext context, Action acceptStrikeBack, Action rejectStrikeBack)
        {
            if(StrikeBack)
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
