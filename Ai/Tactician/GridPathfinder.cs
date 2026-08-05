using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Terrain occupancy over the table at a coarse cell size, inflated by a base radius so a cell is
    /// "blocked" when a model base CENTERED there would touch impassible terrain (#191 A3b - matches
    /// the swept-path validator's Minkowski inflation). Get instances through
    /// <see cref="TerrainGridCache"/> unless the terrain set is transient: "built per query" was
    /// measured cheap on sparse maps, but the profiler evidence plan G6 asked for arrived with the
    /// #268 dense palettes - per-activation rebuilds were ~half a horde game's total CPU.
    /// </summary>
    public sealed class TerrainGrid
    {
        public const float CellSizeInches = 1f;

        private readonly bool[] _blocked;
        private readonly bool[] _difficult;

        public int Cols { get; }
        public int Rows { get; }

        private TerrainGrid(int cols, int rows, bool[] blocked, bool[] difficult)
        {
            Cols = cols;
            Rows = rows;
            _blocked = blocked;
            _difficult = difficult;
        }

        /// <param name="ignoreDifficultTerrain">
        /// Strider (<see cref="ETerrainIgnoreScope.DifficultOnly"/>): leave difficult cells unmarked so
        /// the router stops paying <see cref="GridPathfinder"/>'s difficult multiplier for ground this
        /// unit crosses at full speed. Impassible blocking is unaffected - Strider does not walk
        /// through walls. Flying units want no grid at all (their route is the straight line), so they
        /// are handled by their callers, not here.
        /// </param>
        public static TerrainGrid Build(IReadOnlyList<ITerrain> terrain, float baseRadiusInches,
            bool ignoreDifficultTerrain = false,
            float tableWidthInches = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
            float tableHeightInches = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES)
        {
            int cols = (int)MathF.Ceiling(tableWidthInches / CellSizeInches);
            int rows = (int)MathF.Ceiling(tableHeightInches / CellSizeInches);
            var blocked = new bool[cols * rows];
            var difficult = new bool[cols * rows];

            foreach (ITerrain piece in terrain)
            {
                bool isImpassible = piece.TerrainType.HasFlag(ETerrainType.Impassible);
                bool isDifficult = piece.TerrainType.HasFlag(ETerrainType.Difficult)
                    && !ignoreDifficultTerrain;
                if (!isImpassible && !isDifficult) continue;

                for (int row = 0; row < rows; row++)
                {
                    for (int col = 0; col < cols; col++)
                    {
                        var center = new Float2((col + 0.5f) * CellSizeInches, (row + 0.5f) * CellSizeInches);
                        // Degenerate swept-disc = "does a base centered here touch the piece".
                        if (!piece.Shape.DoesPathIntersectZone(center, center, baseRadiusInches)) continue;
                        if (isImpassible) blocked[row * cols + col] = true;
                        if (isDifficult) difficult[row * cols + col] = true;
                    }
                }
            }

            return new TerrainGrid(cols, rows, blocked, difficult);
        }

        public bool IsBlocked(int col, int row) => _blocked[row * Cols + col];
        public bool IsDifficult(int col, int row) => _difficult[row * Cols + col];

        public (int Col, int Row) ToCell(Position position) => (
            Math.Clamp((int)(position.x / CellSizeInches), 0, Cols - 1),
            Math.Clamp((int)(position.z / CellSizeInches), 0, Rows - 1));

        public Position CellCenter(int col, int row) =>
            new Position((col + 0.5f) * CellSizeInches, (row + 0.5f) * CellSizeInches);
    }

    /// <summary>
    /// Per-game memo for <see cref="TerrainGrid.Build"/> (#191 perf pass): the grid depends only on
    /// the terrain set, the base radius and the Strider flag, yet every activation rebuilt it at
    /// least twice (the planner's route grid and the generator's shared grid) - a handful of
    /// distinct (radius, flag) pairs cover a whole game. Keyed on the table state so concurrent
    /// FdgLab games never share entries; the terrain COUNT rides in the key, so a piece added or
    /// removed mid-game gets a fresh grid while a piece MOVED in place would not - nothing moves
    /// terrain today, revisit the key if something ever does.
    /// </summary>
    public static class TerrainGridCache
    {
        private sealed class Entry
        {
            public readonly Dictionary<(float Radius, bool IgnoreDifficult, int Count), TerrainGrid> Grids = new();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ITableState, Entry> PerGame = new();

        public static TerrainGrid Get(ITableState tableState, IReadOnlyList<ITerrain> terrain,
            float baseRadiusInches, bool ignoreDifficultTerrain = false)
        {
            Entry entry = PerGame.GetOrCreateValue(tableState);
            (float, bool, int) key = (baseRadiusInches, ignoreDifficultTerrain, terrain.Count);
            lock (entry.Grids)
            {
                if (entry.Grids.TryGetValue(key, out TerrainGrid? grid)) return grid;
                grid = TerrainGrid.Build(terrain, baseRadiusInches, ignoreDifficultTerrain);
                entry.Grids[key] = grid;
                return grid;
            }
        }
    }

    /// <summary>
    /// A* over a <see cref="TerrainGrid"/> (#191 A3b, plan D5: goal-directed geometry, never random
    /// sampling - this is what threads the narrow hallway the old angular skirting could not).
    /// Deterministic: fixed expansion order, no randomness (G5).
    /// </summary>
    public static class GridPathfinder
    {
        // Difficult terrain costs extra so clear routes of similar length win. This is a route
        // PREFERENCE only - the rules-true consequence (whole move capped at 6") is applied by the
        // caller when the chosen path does cross difficult ground.
        private const float DifficultCostMultiplier = 2f;

        // 8-connected neighbourhood; diagonals must not cut a blocked corner.
        private static readonly (int DCol, int DRow, float Cost)[] Steps =
        {
            (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
            (1, 1, 1.41421356f), (1, -1, 1.41421356f), (-1, 1, 1.41421356f), (-1, -1, 1.41421356f),
        };

        // #264 issue 3: how far out to look for a stand-in when the goal's own cell is blocked.
        // A cell is 1", and the blocked region is only inflated by a base radius, so anything a
        // few cells out is a different goal, not the same one nudged clear.
        private const int GoalRetargetMaxCellRadius = 4;

        /// <summary>
        /// A polyline from <paramref name="start"/> to <paramref name="goal"/> that avoids impassible
        /// terrain (inflated by <paramref name="baseRadiusInches"/>), string-pulled so straight clear
        /// runs collapse to single segments; null when the goal is unreachable. The start cell is never
        /// treated as blocked (a unit standing at a wall's inflation edge must still be able to leave).
        /// The returned list starts at <paramref name="start"/> and ends at <paramref name="goal"/> -
        /// or, when the goal's own cell is blocked and the goal itself is not legally standable, at
        /// the nearest cell that is.
        /// </summary>
        public static List<Position>? FindPath(TerrainGrid grid, IReadOnlyList<ITerrain> terrain,
            Position start, Position goal, float baseRadiusInches)
        {
            // A clear straight shot needs no search - the common case, and it keeps clear-lane
            // candidates identical to what straight construction produces. "Clear" includes the
            // grid's difficult cells (#281): a straight line through mud must reach the A* so a
            // comparably-priced clear detour can win; a Strider grid marks no difficult cells, so
            // for it this stays the pure impassible test.
            if (SegmentClear(terrain, start, goal, baseRadiusInches)
                && !CrossesDifficult(grid, start, goal))
                return new List<Position> { start, goal };

            (int startCol, int startRow) = grid.ToCell(start);
            (int goalCol, int goalRow) = grid.ToCell(goal);
            // #264 issue 3: the grid tests CELL CENTRES, so a goal a base can legally stand on can
            // still land in a blocked cell - a point 0.7" past a wall's face is standable but its
            // cell centre is inside the inflation. Returning null there sent every caller to a
            // wall-piercing straight line (which also structurally killed the #256 S4 snake, since
            // the snake follows that same line). Aim at the nearest cell a base CAN centre in and
            // keep the true goal as the last hop when it is reachable from there.
            bool retargeted = grid.IsBlocked(goalCol, goalRow);
            if (retargeted && !TryFindNearestOpenCell(grid, goal, ref goalCol, ref goalRow))
                return null;

            int cells = grid.Cols * grid.Rows;
            var cameFrom = new int[cells];
            var costSoFar = new float[cells];
            Array.Fill(cameFrom, -1);
            Array.Fill(costSoFar, float.PositiveInfinity);

            int startIndex = startRow * grid.Cols + startCol;
            int goalIndex = goalRow * grid.Cols + goalCol;
            costSoFar[startIndex] = 0f;

            var open = new PriorityQueue<int, float>();
            open.Enqueue(startIndex, Octile(startCol, startRow, goalCol, goalRow));

            while (open.TryDequeue(out int current, out _))
            {
                if (current == goalIndex) break;
                int col = current % grid.Cols;
                int row = current / grid.Cols;

                foreach ((int dCol, int dRow, float stepCost) in Steps)
                {
                    int nextCol = col + dCol;
                    int nextRow = row + dRow;
                    if (nextCol < 0 || nextCol >= grid.Cols || nextRow < 0 || nextRow >= grid.Rows) continue;
                    if (grid.IsBlocked(nextCol, nextRow)) continue;
                    // No corner cutting: a diagonal requires both orthogonal neighbours clear.
                    if (dCol != 0 && dRow != 0
                        && (grid.IsBlocked(col + dCol, row) || grid.IsBlocked(col, row + dRow))) continue;

                    float multiplier = grid.IsDifficult(nextCol, nextRow) ? DifficultCostMultiplier : 1f;
                    float newCost = costSoFar[current] + stepCost * multiplier;
                    int next = nextRow * grid.Cols + nextCol;
                    if (newCost >= costSoFar[next]) continue;

                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    open.Enqueue(next, newCost + Octile(nextCol, nextRow, goalCol, goalRow));
                }
            }

            if (cameFrom[goalIndex] < 0 && goalIndex != startIndex) return null;

            // Reconstruct as cell centres, with the exact endpoints substituted at both ends.
            var cellsOnPath = new List<int>();
            for (int index = goalIndex; index >= 0; index = cameFrom[index])
            {
                cellsOnPath.Add(index);
                if (index == startIndex) break;
            }
            cellsOnPath.Reverse();

            var waypoints = new List<Position> { start };
            for (int i = 1; i < cellsOnPath.Count - 1; i++)
                waypoints.Add(grid.CellCenter(cellsOnPath[i] % grid.Cols, cellsOnPath[i] / grid.Cols));

            if (!retargeted)
            {
                waypoints.Add(goal);
            }
            else
            {
                // The stand-in cell is where a base can legally centre; the true goal may still be
                // legally standable just past it, so keep it whenever that last hop is walkable.
                Position approach = grid.CellCenter(goalCol, goalRow);
                waypoints.Add(approach);
                if (SegmentClear(terrain, approach, goal, baseRadiusInches)) waypoints.Add(goal);
            }

            return StringPull(terrain, waypoints, baseRadiusInches, grid);
        }

        /// <summary>
        /// The open cell nearest the goal POINT (not the goal cell), searched in expanding rings so
        /// the answer is deterministic and bounded. False when the goal sits deep inside a piece.
        /// </summary>
        private static bool TryFindNearestOpenCell(TerrainGrid grid, Position goal,
            ref int goalCol, ref int goalRow)
        {
            int fromCol = goalCol, fromRow = goalRow;
            for (int radius = 1; radius <= GoalRetargetMaxCellRadius; radius++)
            {
                int bestCol = -1, bestRow = -1;
                float bestDistanceSq = float.PositiveInfinity;
                for (int row = fromRow - radius; row <= fromRow + radius; row++)
                {
                    for (int col = fromCol - radius; col <= fromCol + radius; col++)
                    {
                        // Ring only - the interior was covered by a smaller radius.
                        if (Math.Abs(row - fromRow) != radius && Math.Abs(col - fromCol) != radius) continue;
                        if (col < 0 || col >= grid.Cols || row < 0 || row >= grid.Rows) continue;
                        if (grid.IsBlocked(col, row)) continue;

                        Position centre = grid.CellCenter(col, row);
                        float dx = centre.x - goal.x, dz = centre.z - goal.z;
                        float distanceSq = dx * dx + dz * dz;
                        if (distanceSq >= bestDistanceSq) continue;
                        bestDistanceSq = distanceSq;
                        bestCol = col;
                        bestRow = row;
                    }
                }
                if (bestCol < 0) continue;
                goalCol = bestCol;
                goalRow = bestRow;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The best available route when <see cref="FindPath"/> finds none (a sealed start pocket, a
        /// goal walled off entirely): a route to the REACHABLE cell closest to the goal. Null when
        /// standing still is genuinely the closest the unit can get.
        /// <para>
        /// #264 issue 3: the alternative every caller used was the straight line through the wall.
        /// That is not a degraded route, it is a wrong one - the ladder then halves it into the wall
        /// face, and the #256 S4 snake, which follows the same line, cannot rescue it either.
        /// </para>
        /// </summary>
        public static List<Position>? FindPathToNearestReachable(TerrainGrid grid,
            IReadOnlyList<ITerrain> terrain, Position start, Position goal, float baseRadiusInches)
        {
            (int startCol, int startRow) = grid.ToCell(start);
            int cells = grid.Cols * grid.Rows;
            var cameFrom = new int[cells];
            var costSoFar = new float[cells];
            Array.Fill(cameFrom, -1);
            Array.Fill(costSoFar, float.PositiveInfinity);

            int startIndex = startRow * grid.Cols + startCol;
            costSoFar[startIndex] = 0f;

            int best = startIndex;
            float bestDistanceSq = DistanceSq(start, goal);

            // Uniform-cost flood over the open region the unit can actually get to; a few thousand
            // cells at worst, and only ever reached on the sealed-pocket path.
            var open = new PriorityQueue<int, float>();
            open.Enqueue(startIndex, 0f);
            while (open.TryDequeue(out int current, out float cost))
            {
                if (cost > costSoFar[current]) continue;
                int col = current % grid.Cols;
                int row = current / grid.Cols;

                foreach ((int dCol, int dRow, float stepCost) in Steps)
                {
                    int nextCol = col + dCol;
                    int nextRow = row + dRow;
                    if (nextCol < 0 || nextCol >= grid.Cols || nextRow < 0 || nextRow >= grid.Rows) continue;
                    if (grid.IsBlocked(nextCol, nextRow)) continue;
                    if (dCol != 0 && dRow != 0
                        && (grid.IsBlocked(col + dCol, row) || grid.IsBlocked(col, row + dRow))) continue;

                    float multiplier = grid.IsDifficult(nextCol, nextRow) ? DifficultCostMultiplier : 1f;
                    float newCost = costSoFar[current] + stepCost * multiplier;
                    int next = nextRow * grid.Cols + nextCol;
                    if (newCost >= costSoFar[next]) continue;

                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    open.Enqueue(next, newCost);

                    float distanceSq = DistanceSq(grid.CellCenter(nextCol, nextRow), goal);
                    if (distanceSq < bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        best = next;
                    }
                }
            }

            if (best == startIndex) return null;

            var cellsOnPath = new List<int>();
            for (int index = best; index >= 0; index = cameFrom[index])
            {
                cellsOnPath.Add(index);
                if (index == startIndex) break;
            }
            cellsOnPath.Reverse();

            var waypoints = new List<Position> { start };
            for (int i = 1; i < cellsOnPath.Count; i++)
                waypoints.Add(grid.CellCenter(cellsOnPath[i] % grid.Cols, cellsOnPath[i] / grid.Cols));
            return StringPull(terrain, waypoints, baseRadiusInches, grid);
        }

        private static float DistanceSq(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        /// <summary>
        /// Walks <paramref name="path"/> up to <paramref name="budgetInches"/> of arc length. Returns
        /// the reached endpoint, the waypoints fully traversed on the way (exclusive of the start
        /// point - these become the move's intermediate legs), and whether any traversed segment
        /// crosses difficult terrain (the caller then applies the engine's 6" whole-move cap).
        /// </summary>
        public static (Position Endpoint, List<Position> PassedWaypoints, bool CrossesDifficult)
            AdvanceAlongPath(List<Position> path, float budgetInches, IReadOnlyList<ITerrain> terrain,
                float baseRadiusInches)
        {
            var passed = new List<Position>();
            bool crossesDifficult = false;
            float remaining = Math.Max(0f, budgetInches);
            Position current = path[0];

            for (int i = 1; i < path.Count; i++)
            {
                float dx = path[i].x - current.x;
                float dz = path[i].z - current.z;
                float length = MathF.Sqrt(dx * dx + dz * dz);

                Position legEnd = length <= remaining
                    ? path[i]
                    : new Position(current.x + dx / length * remaining, current.z + dz / length * remaining);

                crossesDifficult |= terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Difficult)
                    && t.Shape.DoesPathIntersectZone(
                        new Float2(current.x, current.z), new Float2(legEnd.x, legEnd.z), baseRadiusInches));

                if (length <= remaining)
                {
                    remaining -= length;
                    current = path[i];
                    if (i < path.Count - 1) passed.Add(path[i]);
                    continue;
                }

                return (legEnd, passed, crossesDifficult);
            }

            return (current, passed, crossesDifficult);
        }

        private static float Octile(int aCol, int aRow, int bCol, int bRow)
        {
            int dx = Math.Abs(aCol - bCol), dz = Math.Abs(aRow - bRow);
            return (dx + dz) + (1.41421356f - 2f) * Math.Min(dx, dz);
        }

        // Slack on the cost-preserving pull comparison (#281): well under the surcharge of even a
        // half-cell of difficult ground (0.5" x the 1x extra multiplier), so quantization noise
        // between the sampled metric and the A*'s cell metric never refuses a legitimate pull, and
        // no real mud crossing ever slips under it.
        private const float PullCostSlackInches = 0.1f;

        /// <summary>
        /// Greedy line-of-sight simplification: from each anchor, keep the farthest waypoint reachable
        /// by a clear swept segment. Preserves endpoints; always terminates (worst case advances one).
        /// Public because per-model leg lists get the same treatment (#264 issue 4) - a model that can
        /// reach a later waypoint directly should not walk the dogleg.
        /// <para>
        /// #281: with a <paramref name="grid"/>, a shortcut must also not cost more than the run it
        /// replaces under the router's own difficult-weighted metric - the pull used to re-test
        /// shortcuts with the impassible-only clearance, silently dragging every bend the A* paid
        /// <see cref="DifficultCostMultiplier"/> for straight back through the mud. A shortcut through
        /// difficult ground is still taken when it is genuinely cheaper (the A* itself would route
        /// there); a Strider grid marks no difficult cells, so its pull stays as aggressive as ever.
        /// Null keeps the pure impassible behaviour for callers with no grid in hand.
        /// </para>
        /// </summary>
        public static List<Position> StringPull(IReadOnlyList<ITerrain> terrain,
            List<Position> waypoints, float baseRadiusInches, TerrainGrid? grid = null)
        {
            var result = new List<Position> { waypoints[0] };
            int anchor = 0;
            while (anchor < waypoints.Count - 1)
            {
                int farthest = anchor + 1;
                for (int probe = waypoints.Count - 1; probe > anchor + 1; probe--)
                {
                    if (SegmentClear(terrain, waypoints[anchor], waypoints[probe], baseRadiusInches)
                        && (grid == null || ShortcutKeepsCost(grid, waypoints, anchor, probe)))
                    {
                        farthest = probe;
                        break;
                    }
                }
                result.Add(waypoints[farthest]);
                anchor = farthest;
            }
            return result;
        }

        /// <summary>Whether the straight hop anchor -> probe is no more expensive than the waypoint
        /// run it would replace, priced by <see cref="WeightedLength"/> on both sides.</summary>
        private static bool ShortcutKeepsCost(TerrainGrid grid, List<Position> waypoints,
            int anchor, int probe)
        {
            float run = 0f;
            for (int i = anchor + 1; i <= probe; i++)
                run += WeightedLength(grid, waypoints[i - 1], waypoints[i]);
            return WeightedLength(grid, waypoints[anchor], waypoints[probe]) <= run + PullCostSlackInches;
        }

        /// <summary>
        /// Arc length priced by the same multiplier the A* pays for difficult cells, sampled against
        /// the grid at half-cell steps. Sampling the GRID (not the terrain shapes) keeps the pull and
        /// the A* on one metric - the cells are radius-inflated, so a shortcut grazing the inflation
        /// pays exactly what the search paid to avoid it.
        /// </summary>
        private static float WeightedLength(TerrainGrid grid, Position from, Position to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (length <= 1e-6f) return 0f;
            int steps = Math.Max(1, (int)MathF.Ceiling(length / (TerrainGrid.CellSizeInches * 0.5f)));
            float stepLength = length / steps;
            float total = 0f;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                (int col, int row) = grid.ToCell(
                    new Position(from.x + dx * t, from.z + dz * t));
                total += grid.IsDifficult(col, row) ? stepLength * DifficultCostMultiplier : stepLength;
            }
            return total;
        }

        /// <summary>Whether the straight segment touches any difficult cell, sampled at half-cell
        /// steps like <see cref="WeightedLength"/>.</summary>
        private static bool CrossesDifficult(TerrainGrid grid, Position from, Position to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / (TerrainGrid.CellSizeInches * 0.5f)));
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                (int col, int row) = grid.ToCell(
                    new Position(from.x + dx * t, from.z + dz * t));
                if (grid.IsDifficult(col, row)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a base of this radius can sweep from <paramref name="from"/> to
        /// <paramref name="to"/> without touching impassible terrain. The one walkability predicate
        /// the route machinery shares - routing, per-model leg joins and the score gradient all have
        /// to agree on what "clear" means, or a move is planned along a line the validator rejects.
        /// </summary>
        public static bool SegmentClear(IReadOnlyList<ITerrain> terrain, Position from, Position to,
            float baseRadiusInches)
        {
            var start = new Float2(from.x, from.z);
            var end = new Float2(to.x, to.z);
            return !terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Impassible)
                && t.Shape.DoesPathIntersectZone(start, end, baseRadiusInches));
        }
    }
}
