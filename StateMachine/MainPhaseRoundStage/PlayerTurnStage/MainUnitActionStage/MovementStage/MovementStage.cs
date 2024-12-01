
using System;

namespace FDG.Stages
{

    public class MovementStage : StageBase<IUnitActionContext>
    {
        public const string MOVEMENT_TO_CHOOSE_ACTION_TRANSITION =
                    "MovementToChooseAction";

        public MovementStage(StateMachine stateMachine, IUnitActionContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {

        }

        public override void Enter()
        {
            base.Enter();

            Context.Log($"Chose movement action.");
            Context.GetHandler<IMovementHandler>().Handle(Context, OnMove);
        }

        private void OnMove(float distance)
        {
            //TEMP distance is just for testing.
            Context.RegisterMoveFinished(distance);
            MoveToReconcileEndOfActivation();
        }

        private void MoveToReconcileEndOfActivation()
        {
            SignalEvent(MOVEMENT_TO_CHOOSE_ACTION_TRANSITION);
        }
    }

    public interface IMovementHandler
    {
        public void Handle(IUnitActionContext actionContext, Action<float> finishedTempDist);
    }
}