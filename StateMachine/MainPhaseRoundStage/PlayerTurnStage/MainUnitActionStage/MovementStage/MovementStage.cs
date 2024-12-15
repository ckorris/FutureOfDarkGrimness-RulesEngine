
using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public class MovementStage : ParentStage<IUnitActionContext, IMovementActionContext>
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

        protected override IMovementActionContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            throw new NotImplementedException();
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMovementActionContext> startingChild)
        {
            throw new NotImplementedException();
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