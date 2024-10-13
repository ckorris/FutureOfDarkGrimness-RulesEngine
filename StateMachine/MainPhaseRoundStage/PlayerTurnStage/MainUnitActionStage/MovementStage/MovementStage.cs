
using System;

namespace FDG.StateMachine
{

    public class MovementStage : StateBase<IUnitActionContext>
    {
        public const string MOVEMENT_TO_MELEE_TRANSITION = "MovementToMelee";
        public const string MOVEMENT_TO_RANGED_TRANSITION = "MovementToRanged";
        public const string MOVEMENT_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION =
                    "MovementToReconcileEndOfActivation";

        public MovementStage(StateMachine stateMachine, IUnitActionContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {

        }

        public override void Enter()
        {
            base.Enter();

            Context.Log($"Chose movement action.");
            Context.MovementHandler.Handle(Context, MoveToMelee, MoveToRanged, MoveToReconcileEndOfActivation);
        }

        private void MoveToMelee()
        {
            SignalEvent(MOVEMENT_TO_MELEE_TRANSITION);
        }

        private void MoveToRanged()
        {
            SignalEvent(MOVEMENT_TO_RANGED_TRANSITION);
        }

        private void MoveToReconcileEndOfActivation()
        {
            SignalEvent(MOVEMENT_TO_RECONCILE_END_OF_ACTIVATION_TRANSITION);
        }
    }

    public interface IMovementHandler 
    {
        public void Handle(IUnitActionContext actionContext, Action onChooseMelee, Action onChooseRanged, 
            Action onChooseNonCombat);
    }
}