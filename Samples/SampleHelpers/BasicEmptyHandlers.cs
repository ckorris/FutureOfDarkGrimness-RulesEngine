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

        public void Handle(IUnitActionContext context, List<ActionChoice> actionChoices, Action onPass)
        {
            switch (TestActionChoice)
            {
                case ETestActionChoice.Movement:
                    ActionChoice moveChoice = actionChoices.First(choice => choice.ChoiceName == ChooseActionStage.MOVEMENT_CHOICE_NAME);
                    ActivateOrPassIfCant(context, moveChoice, onPass);
                    break;
                case ETestActionChoice.Melee:
                    ActionChoice chargeChoice = actionChoices.First(choice => choice.ChoiceName == ChooseActionStage.CHARGE_CHOICE_NAME);
                    ActivateOrPassIfCant(context, chargeChoice, onPass);
                    break;
                case ETestActionChoice.Ranged:
                    ActionChoice shootChoice = actionChoices.First(choice => choice.ChoiceName == ChooseActionStage.SHOOT_CHOICE_NAME);
                    ActivateOrPassIfCant(context, shootChoice, onPass);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ActivateOrPassIfCant(IUnitActionContext context, ActionChoice actionChoice, Action onPass)
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
    

    public class BasicTesterDefinePathHandler : IDefinePathHandler
    {
        public void Handle(PathTemplate pathTemplate, Action onTemplateValid)
        {
            //Just don't move. Not moving will pretty much always be valid.
            onTemplateValid.Invoke();

        }
    }

    public class BasicTesterOfferStrikeBackHandler : IOfferStrikeBackHandler
    {
        public  bool StrikeBack;
        public BasicTesterOfferStrikeBackHandler(bool strikeBack)
        {
            StrikeBack = strikeBack;
        }

        public void Handle(ICombatActionContext context, Action acceptStrikeBack, Action rejectStrikeBack)
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

        public void Handle(ICombatActionContext context, List<ActionChoice> actionChoices, Action onCancel)
        {
            //Just choose the first.
            actionChoices.First(choice => choice.CanActivate).Choose();
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

    public class BasicTesterMapSetupHandler : IMapSetupHandler
    {
        public void Handle(IGameContext context, Action<IGameContext> exitStage)
        {
            //Just accepts it. Will need to actually implement later.
            exitStage.Invoke(context);
        }
    }

    public class BasicDeploymentHandler : IDeploymentHandler
    {
        public void Handle(IGameContext context, Action<IGameContext> exitStage)
        {
            //Just accepts it. Will need to actually implement later.
            exitStage.Invoke(context);
        }
    }

    public class BasicReconcileNewTurnHandler : IReconcileNewTurnHandler
    {
        public void Handle(IMainPhaseContext context, Action<IMainPhaseContext> exitStage)
        {
            //Just accepts it. Will need to actually implement later.
            exitStage.Invoke(context);
        }
    }

    public class BasicStartOfTurnExtraActionsHandler : IStartOfTurnExtraActionsHandler
    {
        public void Handle(IMainPhaseContext context, Action<IMainPhaseContext> exitStage)
        {
            //Just accepts it. Will need to actually implement later.
            exitStage.Invoke(context);
        }
    }

    public class BasicChooseUnitToActivateHandler : IChooseUnitToActivateHandler
    {
        public void Handle(IPlayerTurnContext context, List<ActionChoice> unitChoices)
        {
            //Just choose the first.
            unitChoices.First(choice => choice.CanActivate).Choose();
        }
    }
}
