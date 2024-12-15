
using System;

namespace FDG.Stages
{

    public class MovementStage : StageBase<IUnitActionContext>
    {
        public StageBinding OnFinishedMovement;

        public MovementStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            OnFinishedMovement = new StageBinding(this);
        }

        public override void Enter(IUnitActionContext context)
        {
            GameContext.Log($"Chose movement action.");
            GameContext.GetHandler<IMovementHandler>().Handle(context, (distance) => OnMove(context, distance));
        }

        private void OnMove(IUnitActionContext context, float distance)
        {
            //TEMP distance is just for testing.
            context.RegisterMoveFinished(distance);
            OnFinishedMovement.Activate(context);
        }
    }

    public interface IMovementHandler
    {
        public void Handle(IUnitActionContext actionContext, Action<float> finishedTempDist);
    }
}