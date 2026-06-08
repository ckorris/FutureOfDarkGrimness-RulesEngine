
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

            // Capture each model's full traversed polyline before committing, so the beat can
            // animate around corners exactly as the path was validated (per-segment). The polyline
            // is [current position] + the committed waypoints; after CommitPositions the
            // authoritative position is the final waypoint.
            UnitData movingUnit = context.MovingUnit.GetValue();
            List<ModelMove> moves = new List<ModelMove>(paths.Count);
            foreach (ModelMoveEntry entry in paths)
            {
                if (entry.Positions.Count == 0) continue;
                ModelData model = entry.Model.GetValue();

                List<Position> waypoints = new List<Position>(entry.Positions.Count + 1);
                waypoints.Add(model.Position);       // start
                waypoints.AddRange(entry.Positions); // through to destination
                moves.Add(new ModelMove(model.ID, waypoints));
            }

            MovementExecutor.CommitPositions(paths);

            if (moves.Count > 0)
            {
                // One total duration for the whole move; the renderer spreads it across segments by
                // length. Constant for now — work item 052 leaves room to scale by distance/action.
                await GameContext.Presenter.Present(
                    new UnitMovedBeat(movingUnit.ID, movingUnit.Name, moves, PresentationDurations.UnitMove));
            }

            OnMoveExecuted.Activate(context);
        }
    }

    public interface IExecuteMoveHandler
    {
        public void Handle(Action onMovementShown);
    }
}

