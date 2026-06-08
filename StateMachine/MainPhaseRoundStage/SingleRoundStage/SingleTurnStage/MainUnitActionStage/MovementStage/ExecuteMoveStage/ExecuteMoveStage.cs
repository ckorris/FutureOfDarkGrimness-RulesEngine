
using FDG.Presentation;
using FDG.Presentation.Beats;
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

            // Capture each model's start position before committing, so the beat carries from→to.
            // (After CommitPositions the authoritative position is the destination.)
            UnitData movingUnit = context.MovingUnit.GetValue();
            List<ModelMove> moves = new List<ModelMove>(paths.Count);
            foreach (ModelMoveEntry entry in paths)
            {
                if (entry.Positions.Count == 0) continue;
                ModelData model = entry.Model.GetValue();
                moves.Add(new ModelMove(model.ID, model.Position, entry.Positions[entry.Positions.Count - 1]));
            }

            MovementExecutor.CommitPositions(paths);

            if (moves.Count > 0)
            {
                await GameContext.Presenter.Present(new UnitMovedBeat(movingUnit.ID, movingUnit.Name, moves));
            }

            OnMoveExecuted.Activate(context);
        }
    }

    public interface IExecuteMoveHandler
    {
        public void Handle(Action onMovementShown);
    }
}

