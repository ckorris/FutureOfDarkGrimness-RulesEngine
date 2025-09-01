
using FDG.StageResolution.Requests;

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

        public override async Task Enter(IMovementActionContext context)
        {
            bool gotPaths = context.TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths);

            if (gotPaths == false)
            {
                throw new InvalidOperationException($"Entered {nameof(ExecuteMoveStage)} before paths were set."); 
            }

            foreach(ModelMoveEntry modelEntry in paths)
            {
                if(modelEntry.Positions.Count > 0)
                {
                    //Setting each position may be redundant for awhile, but we might add some kind of animation
                    //where the position updates queue up. So, we'll do this anyway.
                    for (int i = 0; i < modelEntry.Positions.Count; i++)
                    {
                        modelEntry.Model.GetValue().SetPosition(modelEntry.Positions[i]);
                    }
                }
            }

            OnMoveExecuted.Activate(context);
        }
    }

    public interface IExecuteMoveHandler
    {
        public void Handle(Action onMovementShown);
    }
}

