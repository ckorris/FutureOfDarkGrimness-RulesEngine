
using System;
using System.Collections.Generic;
using System.Linq;

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
            bool gotPaths = context.TryGetPaths(out IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths);

            if (gotPaths == false)
            {
                throw new InvalidOperationException($"Entered {nameof(ExecuteMoveStage)} before paths were set."); 
            }

            foreach(KeyValuePair<IModel, IReadOnlyList<Position>> kvp in paths)
            {
                if(kvp.Value.Count > 0)
                {
                    kvp.Key.SetPosition(kvp.Value.Last());
                }
            }
            //For now, just give the base a chance to show that the models moved.

            GameContext.GetHandler<IExecuteMoveHandler>().Handle(() => OnMoveExecuted.Activate(context));
        }
    }

    public interface IExecuteMoveHandler
    {
        public void Handle(Action onMovementShown);
    }
}

