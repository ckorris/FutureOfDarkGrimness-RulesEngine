namespace FDG.Stages
{
    public class PlaceObjectivesStage : StageBase<IGameContext>
    {
        public StageBinding OnObjectivesPlaced;

        private const float MinObjectiveSeparationInches = 9f;
        private const float CandidateGridStepInches = 3f;
        private const float TableEdgeMarginInches = 2f;

        private readonly Func<int> _getObjectiveCount;

        public PlaceObjectivesStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent,
            Func<int> getObjectiveCount)
            : base(gameContext, parent)
        {
            OnObjectivesPlaced = new StageBinding(this);
            _getObjectiveCount = getObjectiveCount;
        }

        public override async Task Enter(IGameContext context)
        {
            int count = _getObjectiveCount();
            context.Log($"Auto-placing {count} objectives.");

            var tableState = context.TableState;
            float tableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float deployDepth = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;

            // Objectives must land in the middle band — outside both deployment zones.
            float zMin = deployDepth + TableEdgeMarginInches;
            float zMax = tableH - deployDepth - TableEdgeMarginInches;

            var impassable = tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();

            // Build and shuffle candidate grid positions.
            var candidates = new List<Float2>();
            for (float x = TableEdgeMarginInches; x <= tableW - TableEdgeMarginInches; x += CandidateGridStepInches)
                for (float z = zMin; z <= zMax; z += CandidateGridStepInches)
                    candidates.Add(new Float2(x, z));

            var rng = new Random();
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            // Greedily pick positions that satisfy separation and terrain constraints.
            var placed = new List<Float2>();
            foreach (var candidate in candidates)
            {
                if (placed.Count >= count) break;
                if (IsTooCloseToExisting(candidate, placed)) continue;
                if (IsInImpassableTerrain(candidate, impassable)) continue;
                placed.Add(candidate);
            }

            if (placed.Count < count)
                context.Log($"Warning: could only place {placed.Count}/{count} objectives (terrain may be blocking).");

            foreach (var pos in placed)
                context.GameDataStore.Create(new ObjectiveData(new Position(pos.X, pos.Y), context.GameDataStore));

            context.Log($"Placed {placed.Count} objectives.");
            OnObjectivesPlaced.Activate(context);
        }

        private static bool IsTooCloseToExisting(Float2 candidate, List<Float2> placed)
        {
            foreach (var p in placed)
            {
                float dx = candidate.X - p.X;
                float dz = candidate.Y - p.Y;
                if (MathF.Sqrt(dx * dx + dz * dz) < MinObjectiveSeparationInches)
                    return true;
            }
            return false;
        }

        private static bool IsInImpassableTerrain(Float2 candidate, List<ITerrain> impassable)
        {
            foreach (var t in impassable)
                if (t.IsPointWithinZone(candidate))
                    return true;
            return false;
        }
    }
}
