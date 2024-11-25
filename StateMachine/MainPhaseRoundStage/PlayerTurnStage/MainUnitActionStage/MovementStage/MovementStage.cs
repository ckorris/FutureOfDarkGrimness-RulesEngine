
using System;

namespace FDG.Stages
{

    public class MovementStage : StateBase<IUnitActionContext>
    {
        public const string MOVEMENT_TO_CHOOSE_ACTION_TRANSITION =
                    "MovementToChooseAction";

        public MovementStage(StateMachine stateMachine, IUnitActionContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {

        }

        public override void Enter()
        {
            base.Enter();

            Context.Log($"Chose movement action.");
            Context.GetHandler<IMovementHandler>().Handle(Context, MoveToReconcileEndOfActivation);
        }

        private void MoveToReconcileEndOfActivation()
        {
            SignalEvent(MOVEMENT_TO_CHOOSE_ACTION_TRANSITION);
        }
    }

    public interface IMovementHandler
    {
        public void Handle(IUnitActionContext actionContext, Action finishedTemp);
    }
}