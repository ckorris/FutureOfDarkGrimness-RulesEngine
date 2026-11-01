using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Plays out the planner's cached macro-action when the engine asks for the actual move
    /// (#191 A4-2). The cached move was built by the MovementPlanner ladder, so it is engine-valid
    /// by construction - but it is re-checked against THIS request's budgets before submitting,
    /// because a rejection would fault the stage with no retry; anything without a valid plan
    /// falls back to the solo-rules movement resolver (G3).
    /// </summary>
    public class TacticianMovementResolver
        : IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>
    {
        private readonly IMovePlanSource _planner;
        private readonly ITableState _tableState;
        private readonly IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>> _soloFallback;
        // #216 / #264 issue 6: a silent degradation to solo is the failure mode behind several
        // walled-unit reports - the unit just looks passive and nothing says why. Null in normal
        // play; wired to the same analysis sink as the planner's decision table.
        private readonly Action<string>? _log;

        public TacticianMovementResolver(IMovePlanSource planner, ITableState tableState,
            IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>> soloFallback,
            Action<string>? log = null)
        {
            _planner = planner;
            _tableState = tableState;
            _soloFallback = soloFallback;
            _log = log;
        }

        public Task<CancellableResult<List<ModelMoveEntry>>> Resolve(DefineMovementPathRequest request)
        {
            // #442: an optional triggered move at activation start (#434's reposition family) comes BEFORE Choose
            // Action, so there is no cached plan yet - ask the planner where the activation is heading.
            if (!request.MainActivationMove && request.AllowCancel && _planner is TacticianPlanner tactician)
            {
                CancellableResult<List<ModelMoveEntry>>? aimed = ResolveActivationStartMove(request, tactician);
                if (aimed != null) return Task.FromResult(aimed);
            }

            List<ModelMoveEntry>? planned = _planner.TakePlannedMove(request.UnitDataBinding);
            if (planned == null)
                return DegradeToSolo(request, "no cached plan (Hold/Pass, or the planner had no claim)");

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };
            var enemies = MovementPlanner.LiveEnemyFootprints(_tableState,
                request.UnitDataBinding.GetValue().PlayerID);
            // #205: reject a planned macro-move that would end stacked on a friendly, falling back to the solo
            // resolver (which re-plans around friendlies) rather than submitting a move the stage rejects.
            var friendlies = MovementPlanner.LiveFriendlyFootprints(_tableState,
                request.UnitDataBinding.GetValue().PlayerID, request.UnitDataBinding.GetValue().ID);
            // #159: lenientCoherency mirrors DefinePathStage so this pre-check agrees with the authoritative
            // one - a plan that holds an already-broken unit isn't rejected for a coherency it can't restore.
            bool valid = MovementUtilities.ValidatePaths(planned, budgetFor, enemies,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects.ToList(), out _, friendlies,
                lenientCoherency: true);

            if (valid)
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(
                    new Selected<List<ModelMoveEntry>>(planned));

            // #264 issue 7 (with #216's repair pass): the plan was built at Choose Action time, before
            // this request existed, so its budgets can disagree with the request's per-model ones - a
            // joined Slow hero makes EVERY planned move fail here, and the unit then plays the solo
            // resolver for the whole game without anything saying so. The macro choice was still the
            // right one; only its distance was wrong. Re-run the same ladder toward the same
            // destination under THIS request's budgets before conceding the activation.
            List<ModelMoveEntry>? repaired = TryRepairWithinRequestBudgets(request, planned, budgetFor,
                enemies, friendlies);
            if (repaired != null)
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(
                    new Selected<List<ModelMoveEntry>>(repaired));

            return DegradeToSolo(request, "the planned move failed this request's per-model re-check "
                + "and could not be re-planned within its budgets");
        }

        // Null: the planner has no claim (the existing path decides). Cancelled: the plan holds - the legal decline of
        // an optional move. Otherwise the move toward the plan's endpoint, validated here exactly as the engine will,
        // and declined instead when no real move (over a few hundredths of an inch) survives the ladder.
        private CancellableResult<List<ModelMoveEntry>>? ResolveActivationStartMove(DefineMovementPathRequest request,
            TacticianPlanner tactician)
        {
            DataBinding<UnitData> unit = request.UnitDataBinding;
            (TacticianPlanner.ETriggeredMoveAim kind, Position goal) = tactician.AimActivationStartMove(unit);
            if (kind == TacticianPlanner.ETriggeredMoveAim.NoClaim) return null;
            if (kind == TacticianPlanner.ETriggeredMoveAim.Stay) return new Cancelled<List<ModelMoveEntry>>();

            var living = unit.GetValue().ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return new Cancelled<List<ModelMoveEntry>>();
            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };
            float budget = Math.Max(0f, request.MaxDistanceInches - 0.001f);
            var enemies = MovementPlanner.LiveEnemyFootprints(_tableState, unit.GetValue().PlayerID);
            var friendlies = MovementPlanner.LiveFriendlyFootprints(_tableState, unit.GetValue().PlayerID,
                unit.GetValue().ID);
            List<ITerrain> terrain = _tableState.Terrain.Objects.ToList();
            // Strict coherency, as MovementExecutor.TryMove checks a triggered move (it has no lenient arm).
            bool Valid(List<ModelMoveEntry> candidate) => MovementUtilities.ValidatePaths(candidate, budgetFor, enemies,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain,
                terrain, out _, friendlies);

            Position start = new Position(living.Average(m => m.GetValue().Position.x),
                living.Average(m => m.GetValue().Position.z));
            List<ModelMoveEntry>? best = null;
            float bestRemaining = Distance(start, goal) - 0.05f; // a real move must close at least this much

            // A short move is best spent as a RIGID shift: the planner's candidate builders re-pack the unit into its
            // formation at the end, which on a 1-3" budget costs most of the move (a column re-forming into a grid).
            // Straight at the goal, longest first; the route-following move below covers a wall in the way.
            float gap = Distance(start, goal);
            if (gap > 0.001f)
            {
                float dx = (goal.x - start.x) / gap, dz = (goal.z - start.z) / gap;
                foreach (float fraction in RigidStepFractions)
                {
                    float step = Math.Min(budget, gap) * fraction;
                    List<ModelMoveEntry> shift = living.Select(m => new ModelMoveEntry(m, new List<Position>
                        { new Position(m.GetValue().Position.x + dx * step, m.GetValue().Position.z + dz * step) }))
                        .ToList();
                    if (!Valid(shift)) continue;
                    float remaining = gap - step;
                    if (remaining < bestRemaining) { best = shift; bestRemaining = remaining; }
                    break;
                }
            }

            List<ModelMoveEntry> routed = MovementPlanner.PlanMoveToward(unit, living, _tableState, goal, budget,
                request.MaxDistanceInches, budgetFor, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain);
            if (Valid(routed))
            {
                var ends = routed.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
                if (ends.Count > 0)
                {
                    float remaining = Distance(new Position(ends.Average(p => p.x), ends.Average(p => p.z)), goal);
                    if (remaining < bestRemaining) { best = routed; bestRemaining = remaining; }
                }
            }

            if (best == null)
            {
                _log?.Invoke($"activation-start move {unit.GetValue().Name}: declined (no legal move toward the plan)");
                return new Cancelled<List<ModelMoveEntry>>();
            }
            return new Selected<List<ModelMoveEntry>>(best);
        }

        private static readonly float[] RigidStepFractions = { 1f, 0.75f, 0.5f, 0.25f };

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private Task<CancellableResult<List<ModelMoveEntry>>> DegradeToSolo(
            DefineMovementPathRequest request, string reason)
        {
            _log?.Invoke($"solo-fallback {request.UnitDataBinding.GetValue().Name}: {reason}");
            return _soloFallback.Resolve(request);
        }

        // Re-plan toward the cached move's own destination at the SLOWEST model's allowance, with each
        // model validated against its own cap. Null when even that cannot be made legal (the solo
        // resolver, which re-plans from scratch, is the right answer then).
        private List<ModelMoveEntry>? TryRepairWithinRequestBudgets(DefineMovementPathRequest request,
            List<ModelMoveEntry> planned, Func<ModelMoveEntry, ModelMoveBudget> budgetFor,
            List<EnemyModelFootprint> enemies, List<EnemyModelFootprint> friendlies)
        {
            DataBinding<UnitData> unit = request.UnitDataBinding;
            var living = unit.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return null;

            var ends = planned.Where(e => e.Positions.Count > 0)
                .Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0) return null;
            var goal = new Position(ends.Average(p => p.x), ends.Average(p => p.z));

            float rush = float.MaxValue, hardCap = float.MaxValue;
            foreach (DataBinding<ModelData> model in living)
            {
                var (_, modelRush, modelMax) = request.BudgetFor(model.GetValue().ID);
                rush = Math.Min(rush, modelRush);
                hardCap = Math.Min(hardCap, modelMax);
            }
            if (hardCap <= 0f) return null;

            List<ModelMoveEntry> repaired = MovementPlanner.PlanMoveToward(unit, living, _tableState,
                goal, Math.Max(0f, Math.Min(rush, hardCap) - 0.001f), hardCap, budgetFor,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain);

            bool valid = MovementUtilities.ValidatePaths(repaired, budgetFor, enemies,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects.ToList(), out _,
                friendlies, lenientCoherency: true);
            return valid ? repaired : null;
        }
    }
}
