
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

            MovementExecutor.CommitPositions(paths);

            OnMoveExecuted.Activate(context);
        }
    }

    public interface IExecuteMoveHandler
    {
        public void Handle(Action onMovementShown);
    }
}

