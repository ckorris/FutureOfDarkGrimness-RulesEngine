
using System;

namespace FDG.Stages
{
    public class ExecuteMoveStage : StageBase<IMovementActionContext>
    {
        public StageBinding OnMoveExecuted;

        public ExecuteMoveStage(IGameContext gameContext, IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
            OnMoveExecuted = new StageBinding(this);
        }

        public override void Enter(IMovementActionContext context)
        {
            //For now, just give the base a chance to show that the models moved.
            GameContext.GetHandler<IExecuteMoveHandler>().Handle(() => OnMoveExecuted.Activate(context));
        }
    }

    public interface IExecuteMoveHandler
    {
        public void Handle(Action onMovementShown);
    }
}

