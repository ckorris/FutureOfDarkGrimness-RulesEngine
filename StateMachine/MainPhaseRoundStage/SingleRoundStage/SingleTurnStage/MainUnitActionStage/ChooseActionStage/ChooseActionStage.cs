using FDG.StageResolution.Requests;
using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public class ChooseActionStage : StageBase<IUnitActionContext>
    {
        public StageBinding ToMovement;
        public StageBinding ToCharge;
        public StageBinding ToShoot;
        public StageBinding ToReconcileEndOfActivation;

        public const string MOVEMENT_CHOICE_NAME = "Move";
        public const string CHARGE_CHOICE_NAME = "Charge";
        public const string SHOOT_CHOICE_NAME = "Shoot";
        public const string PASS_CHOICE_NAME = "Pass";

        public ChooseActionStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            ToMovement = new StageBinding(this);
            ToCharge = new StageBinding(this);
            ToShoot = new StageBinding(this);
            ToReconcileEndOfActivation = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
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

            List<string> validOptions = new List<string>();
            List<StringSelectionRequest.InvalidOption> invalidOptions = new List<StringSelectionRequest.InvalidOption>();

            Dictionary<string, Action> outcomes = new Dictionary<string, Action>();

            if(canMove)
            {
                validOptions.Add(MOVEMENT_CHOICE_NAME);
                outcomes.Add(MOVEMENT_CHOICE_NAME, () => ToMovement.Activate(context));
            }
            else
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(MOVEMENT_CHOICE_NAME, cantMoveReason));
            }

            if(canCharge)
            {
                validOptions.Add(CHARGE_CHOICE_NAME);
                outcomes.Add(CHARGE_CHOICE_NAME, () => ToCharge.Activate(context));
            }
            else
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(CHARGE_CHOICE_NAME, cantChargeReason));
            }

            if(canShoot)
            {
                validOptions.Add(SHOOT_CHOICE_NAME);
                outcomes.Add(SHOOT_CHOICE_NAME, () => ToShoot.Activate(context));
            }
            else
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(SHOOT_CHOICE_NAME, cantShootReason));
            }

            //Add any others here somehow.

            //Add pass option.
            validOptions.Add(PASS_CHOICE_NAME);
            outcomes.Add(PASS_CHOICE_NAME, () => ToReconcileEndOfActivation.Activate(context));

            StringSelectionRequest request = new StringSelectionRequest(context.ActivatingPlayer(), "Choose Action", validOptions, invalidOptions);

            string choice = await GameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(context.ActivatingPlayer(), request);
            
            if(outcomes.ContainsKey(choice) == false)
            {
                throw new ArgumentException($"Request option was {choice}, but that wasn't an option.");
            }

            outcomes[choice].Invoke();
        }


        private bool GetCanMove(IUnitActionContext context, out string reasonIfCant)
        {
            if (context.HasMoved == true)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} has already moved.";
                return false;
            }

            if (context.HasAttacked == true)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} has already attacked.";
                return false;
            }

            bool canMoveFromUnit = context.ActivatingUnit.GetValue().GetMobility(out _, out _);

            if (canMoveFromUnit == false)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} is immobile.";

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

            if (context.ActivatingUnit.GetValue().GetMeleeWeapons().Count == 0)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} unit has no melee weapons.";
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

            context.ActivatingUnit.GetValue().GetMobility(out float moveShootDistanceInches, out _);

            if (context.MoveDistance > moveShootDistanceInches)
            {
                reasonIfCant = $"Moved {context.MoveDistance} inches, when max to move and shoot for {context.ActivatingUnit.GetValue().Name} " + 
                    $" is {moveShootDistanceInches}.";
                return false;
            }

            if (context.ActivatingUnit.GetValue().GetRangedWeapons().Count == 0)
            {
                reasonIfCant = $"{context.ActivatingUnit.GetValue().Name} has no ranged weapons.";
                return false;
            }

            reasonIfCant = null;
            return true;
        }


    }
}