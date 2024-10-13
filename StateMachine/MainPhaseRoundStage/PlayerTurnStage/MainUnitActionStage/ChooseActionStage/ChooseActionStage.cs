using System;

namespace FDG.Stages
{

    public class ChooseActionStage : StateBase<IUnitActionContext>
    {
        public const string CHOOSE_ACTION_TO_MOVEMENT_TRANSITION =
            "ChooseActionToMovement";

        public const string CHOOSE_ACTION_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION =
            "ChooseActionToReconcileEndOfActivation";

        public ChooseActionStage(StateMachine stateMachine, IUnitActionContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Entered Choose Action.");

            //Note that in the future, this should get optional actions somehow, like spellcasting.

            Context.ChooseActionHandler.Handle(Context, MoveToMovement, MoveToReconcileEndOfActivation);
        }

        private void MoveToMovement()
        {
            SignalEvent(CHOOSE_ACTION_TO_MOVEMENT_TRANSITION);
        }

        private void MoveToReconcileEndOfActivation()
        {
            SignalEvent(CHOOSE_ACTION_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION);
        }

    }

    public interface IChooseActionHandler
    {
        public void Handle(IUnitActionContext context, Action chooseMovement, Action pass);
    }
}