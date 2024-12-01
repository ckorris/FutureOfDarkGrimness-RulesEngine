using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public class ChooseActionStage : StageBase<IUnitActionContext>
    {
        public const string CHOOSE_ACTION_TO_MOVEMENT_TRANSITION =
            "ChooseActionToMovement";

        public const string CHOOSE_ACTION_TO_CHARGE_TRANSITION =
            "ChooseActionToCharge";

        public const string CHOOSE_ACTION_TO_SHOOT_TRANSITION =
            "ChooseActionToShoot";

        public const string CHOOSE_ACTION_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION =
            "ChooseActionToReconcileEndOfActivation";

        public StageBinding ToMovement;
        public StageBinding ToCharge;
        public StageBinding ToShoot;
        public StageBinding ToReconcileEndOfActivation;

        public const string MOVEMENT_CHOICE_NAME = "Move";
        public const string CHARGE_CHOICE_NAME = "Charge";
        public const string SHOOT_CHOICE_NAME = "Shoot";

        public ChooseActionStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            ToMovement = new StageBinding(this);
            ToCharge = new StageBinding(this);
            ToShoot = new StageBinding(this);
            ToReconcileEndOfActivation = new StageBinding(this);
        }

        public override void Enter(IUnitActionContext context)
        {

            GameContext.Log("Entered Choose Action.");

            //Note that in the future, this should get optional actions somehow, like spellcasting.

            bool canMove = GetCanMove(context, out string cantMoveReason);
            bool canCharge = GetCanCharge(context, out string cantChargeReason);
            bool canShoot = GetCanShoot(context, out string cantShootReason);
            bool hasCustomActionsAvailable = false; //TODO: Implement.

            //If we have no available actions 
            if ((canMove || canCharge || canShoot || hasCustomActionsAvailable) == false)
            {
                GameContext.Log($"No more available actions left in {nameof(ChooseActionStage)}. Passing.");
                ToReconcileEndOfActivation.Activate(context);
                return;
            }

            List<ActionChoice> actionChoices = new List<ActionChoice>()
            {
                new ActionChoice(() => ToMovement.Activate(context), MOVEMENT_CHOICE_NAME, canMove, canMove ? "" : cantMoveReason),
                new ActionChoice(() => ToCharge.Activate(context), CHARGE_CHOICE_NAME, canCharge, canCharge ? "" : cantChargeReason),
                new ActionChoice(() => ToShoot.Activate(context), SHOOT_CHOICE_NAME, canShoot, canShoot ? "" : cantShootReason)
            };


            GameContext.GetHandler<IChooseActionHandler>().Handle(context, actionChoices, () => ToReconcileEndOfActivation.Activate(context));
        }


        private bool GetCanMove(IUnitActionContext context, out string reasonIfCant)
        {
            if (context.HasMoved == true)
            {
                reasonIfCant = $"{context.ActivatingUnit.Name} has already moved.";
                return false;
            }

            if (context.HasAttacked == true)
            {
                reasonIfCant = $"{context.ActivatingUnit.Name} has already attacked.";
                return false;
            }

            bool canMoveFromUnit = context.ActivatingUnit.GetMobility(out _, out _);

            if (canMoveFromUnit == false)
            {
                reasonIfCant = $"{context.ActivatingUnit.Name} is immobile.";

                return false;
            }

            reasonIfCant = null;
            return true;
        }

        private bool GetCanCharge(IUnitActionContext context, out string reasonIfCant)
        {
            if (context.HasAttacked)
            {
                reasonIfCant = "Has already attacked.";
                return false;
            }

            if (context.ActivatingUnit.GetMeleeWeapons().Count == 0)
            {
                reasonIfCant = $"{context.ActivatingUnit.Name} unit has no melee weapons.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }

        private bool GetCanShoot(IUnitActionContext context, out string reasonIfCant)
        {
            if (context.HasAttacked)
            {
                reasonIfCant = "Has already attacked.";
                return false;
            }

            context.ActivatingUnit.GetMobility(out float moveShootDistanceInches, out _);

            if (context.MoveDistance > moveShootDistanceInches)
            {
                reasonIfCant = $"Moved {context.MoveDistance} inches, when max to move and shoot for {context.ActivatingUnit.Name} is {moveShootDistanceInches}.";
                return false;
            }

            if (context.ActivatingUnit.GetRangedWeapons().Count == 0)
            {
                reasonIfCant = $"{context.ActivatingUnit.Name} has no ranged weapons.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }


    }

    public interface IChooseActionHandler
    {
        public void Handle(IUnitActionContext context, List<ActionChoice> actionChoices, Action onPass);
    }
}