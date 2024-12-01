using System;
using System.Collections.Generic;
using System.Windows.Documents;

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

        public const string MOVEMENT_CHOICE_NAME = "Move";
        public const string CHARGE_CHOICE_NAME = "Charge";
        public const string SHOOT_CHOICE_NAME = "Shoot";

        public ChooseActionStage(StateMachine stateMachine, IUnitActionContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Entered Choose Action.");

            //Note that in the future, this should get optional actions somehow, like spellcasting.
            
            bool canMove = GetCanMove(out string cantMoveReason);
            bool canCharge = GetCanCharge(out string cantChargeReason);
            bool canShoot = GetCanShoot(out string cantShootReason);
            bool hasCustomActionsAvailable = false; //TODO: Implement.

            //If we have no available actions 
            if((canMove || canCharge || canShoot || hasCustomActionsAvailable) == false)
            {
                Context.Log($"No more available actions left in {nameof(ChooseActionStage)}. Passing.");
                MoveToReconcileEndOfActivation();
                return;
            }

            List<ActionChoice> actionChoices = new List<ActionChoice>()
            {
                new ActionChoice(MoveToMovement, MOVEMENT_CHOICE_NAME, canMove, canMove ? "" : cantMoveReason),
                new ActionChoice(MoveToCharge, CHARGE_CHOICE_NAME, canCharge, canCharge ? "" : cantChargeReason),
                new ActionChoice(MoveToShoot, SHOOT_CHOICE_NAME, canShoot, canShoot ? "" : cantShootReason)
            };


            Context.GetHandler < IChooseActionHandler>().Handle(Context, actionChoices, MoveToReconcileEndOfActivation);
        }


        private void MoveToMovement()
        {
            SignalEvent(CHOOSE_ACTION_TO_MOVEMENT_TRANSITION);
        }

        private void MoveToCharge()
        {
            SignalEvent(CHOOSE_ACTION_TO_CHARGE_TRANSITION);
        }

        private void MoveToShoot()
        {
            SignalEvent(CHOOSE_ACTION_TO_SHOOT_TRANSITION);
        }

        private void MoveToReconcileEndOfActivation()
        {
            SignalEvent(CHOOSE_ACTION_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION);
        }

        private bool GetCanMove(out string reasonIfCant)
        {
            if (Context.HasMoved == true)
            {
                reasonIfCant = $"{Context.ActivatingUnit.Name} has already moved.";
                return false;
            }

            if (Context.HasAttacked == true)
            {
                reasonIfCant = $"{Context.ActivatingUnit.Name} has already attacked.";
                return false;
            }

            bool canMoveFromUnit = Context.ActivatingUnit.GetMobility(out _, out _);

            if (canMoveFromUnit == false)
            {
                reasonIfCant = $"{Context.ActivatingUnit.Name} is immobile.";

                return false;
            }

            reasonIfCant = null;
            return true;
        }

        private bool GetCanCharge(out string reasonIfCant)
        {
            if (Context.HasAttacked)
            {
                reasonIfCant = "Has already attacked.";
                return false;
            }

            if (Context.ActivatingUnit.GetMeleeWeapons().Count == 0)
            {
                reasonIfCant = $"{Context.ActivatingUnit.Name} unit has no melee weapons.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }

        private bool GetCanShoot(out string reasonIfCant)
        {
            if (Context.HasAttacked)
            {
                reasonIfCant = "Has already attacked.";
                return false;
            }

            Context.ActivatingUnit.GetMobility(out float moveShootDistanceInches, out _);

            if (Context.MoveDistance > moveShootDistanceInches)
            {
                reasonIfCant = $"Moved {Context.MoveDistance} inches, when max to move and shoot for {Context.ActivatingUnit.Name} is {moveShootDistanceInches}.";
                return false;
            }

            if (Context.ActivatingUnit.GetRangedWeapons().Count == 0)
            {
                reasonIfCant = $"{Context.ActivatingUnit.Name} has no ranged weapons.";
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