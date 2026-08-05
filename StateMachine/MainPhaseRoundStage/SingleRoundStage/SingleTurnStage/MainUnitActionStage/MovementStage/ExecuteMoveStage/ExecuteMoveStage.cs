
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
            // #294: the heaviest model actually on the move, for the beat's weight proxy. Read off
            // the moving models rather than the unit so a joined Tough hero sets the tone only while
            // it is one of the models moving.
            int toughness = 1;
            foreach (ModelMoveEntry entry in paths)
            {
                if (entry.Positions.Count == 0) continue;
                ModelData model = entry.Model.GetValue();

                List<Position> waypoints = new List<Position>(entry.Positions.Count + 1);
                waypoints.Add(model.Position);       // start
                waypoints.AddRange(entry.Positions); // through to destination
                moves.Add(new ModelMove(model.ID, waypoints, BeatFacings(model, entry, waypoints.Count)));

                int modelTough = (int)MathF.Round(model.TotalWounds);
                if (modelTough > toughness) toughness = modelTough;
            }

            MovementExecutor.CommitPositions(paths);

            if (moves.Count > 0)
            {
                // One total duration for the whole move (the renderer spreads it across segments by
                // length), scaled by the longest model's path so a longer move takes proportionally
                // longer at a roughly constant on-screen speed.
                float longestPath = 0f;
                foreach (ModelMove move in moves)
                {
                    float len = 0f;
                    for (int i = 1; i < move.Waypoints.Count; i++)
                    {
                        Position a = move.Waypoints[i - 1], b = move.Waypoints[i];
                        float dx = b.x - a.x, dz = b.z - a.z;
                        len += MathF.Sqrt(dx * dx + dz * dz);
                    }
                    if (len > longestPath) longestPath = len;
                }

                await GameContext.Presenter.Present(new UnitMovedBeat(movingUnit.ID, movingUnit.Name, moves,
                    PresentationDurations.ForMoveDistance(longestPath), toughness));
            }

            // Now that the models have arrived, land the dangerous-terrain test ApplyNonMovementTerrainEffects
            // rolled on the way in: the batched dice, then each wound with its death/flinch animation at the
            // model's destination. Deferred to here (rather than applied at roll time) so a casualty completes
            // its move and falls where it stopped, and so no model is ever dead in state without its death
            // beat already enqueued — which is what made dangerous-terrain casualties disappear silently.
            await MovementExecutor.ResolveDangerousTerrain(GameContext, context.PendingDangerousTerrain);

            // #153: the move is resolved — spend one-shot (NextTrigger) grants that keyed on this move.
            // Declaration-time budget projection and path-validation queries are read-only, so a granted
            // movement rule (Shock Speed's +2"/+4") or terrain rule (counts-as Difficult/Dangerous) stays
            // visible for the whole move and is consumed exactly once, here. The action type in the
            // declared context is cosmetic — consumption keys on hook+seat, not on conditions.
            context.TryGetMovementDistance(out float distance);
            Rules.Definitions.EActionType resolvedAction = distance <= context.MaxAdvanceDistance
                ? Rules.Definitions.EActionType.Advance
                : Rules.Definitions.EActionType.Rush;
            GameContext.RuleEvaluator.ConsumeOneShotGrants(
                new Rules.Dispatch.Contexts.MoveActionDeclaredContext(movingUnit, resolvedAction, distance),
                (movingUnit, Rules.Foundation.ERuleSeat.Actor));
            GameContext.RuleEvaluator.ConsumeOneShotGrants(
                new Rules.Dispatch.Contexts.MoveThroughTerrainContext(movingUnit),
                (movingUnit, Rules.Foundation.ERuleSeat.Actor));

            await OnMoveExecuted.Activate(context);
        }

        /// <summary>
        /// #340: the attitude at each of the beat's waypoints, 1:1 with them - index 0 is the model's
        /// PRE-MOVE resting facing (paired with the start position the polyline opens on), the rest are the
        /// per-waypoint facings the path was built with. This must be read BEFORE
        /// <c>MovementExecutor.CommitPositions</c> snaps the model to its final facing, which is why it is
        /// captured in the same loop as the waypoints.
        ///
        /// <para>Null when the move carries no facings (AI, aircraft, holds): nothing turned, so the front
        /// end simply draws the model's own facing for the whole glide. A short list is padded with its last
        /// entry rather than trusted to line up - a beat that mispairs facings with waypoints would spin a
        /// model on screen, and the executor already tolerates the same mismatch.</para>
        /// </summary>
        private static List<Float2>? BeatFacings(ModelData model, ModelMoveEntry entry, int waypointCount)
        {
            if (entry.Facings == null || entry.Facings.Count == 0) return null;

            List<Float2> facings = new List<Float2>(waypointCount) { model.Facing }; // the pose it sets off from
            for (int i = 0; facings.Count < waypointCount; i++)
                facings.Add(i < entry.Facings.Count ? entry.Facings[i] : entry.Facings[^1]);
            return facings;
        }
    }

    public interface IExecuteMoveHandler
    {
        public void Handle(Action onMovementShown);
    }
}

