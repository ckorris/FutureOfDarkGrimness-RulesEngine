using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Auto-places a unit's models as one cohesion-valid block in the deployment zone. The models pack into
    /// a tight, square-ish grid (0.1" base-to-base, the same formation the movement resolvers use via
    /// <see cref="FDG.Stages.CohesiveFormation"/>), so the whole unit always satisfies BOTH cohesion rules —
    /// every model within 1" of a neighbour (<see cref="GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES"/>)
    /// and within 9" of every other model (<see cref="GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES"/>).
    /// A 10-wide single rank would break the 9" rule, so wrapping into ranks falls out of the grid for free.
    ///
    /// Successive units fan out across the zone (own lane, alternating Z bands) so the army spreads instead of
    /// stacking; that lane is only the *preferred* block centre — the resolver then searches outward for a
    /// centre where the intact block clears the zone shape, impassible terrain, already-placed models, and
    /// (for Ambush, when <see cref="PlaceObjectsRequest{T}.MinDistanceFromEnemiesInches"/> &gt; 0) every enemy
    /// model. If no fully-clear centre exists it places the block intact at the best clamped centre — cramped
    /// but never scattered, so a model is never stranded out of cohesion.
    /// </summary>
    public class AiPlaceObjectsResolver<T> : IStageResolver<PlaceObjectsRequest<T>, CancellableResult<List<PlacedObjectEntry<T>>>>
    {
        private readonly ITableState _tableState;
        private readonly Dictionary<PlayerID, int> _deployCountPerPlayer = new();

        // Fan-out spacing: successive units march across the zone in lanes this far apart, then wrap into Z
        // bands this far apart (alternating north/south of centre), so the AI spreads its army across the
        // deployment zone instead of stacking every unit in one spot.
        private const float FanOutLaneSpacingInches = 9f;
        private const float FanOutBandSpacingInches = 4.5f;

        //Snapshot of impassible terrain for the current Resolve call; models can't be placed overlapping it.
        private IReadOnlyList<ITerrain> _impassibleTerrain = System.Array.Empty<ITerrain>();

        public AiPlaceObjectsResolver(ITableState tableState)
        {
            _tableState = tableState;
        }

        /// <summary> The AI always places; it never declines a disembark it chose to make. </summary>
        public async Task<CancellableResult<List<PlacedObjectEntry<T>>>> Resolve(PlaceObjectsRequest<T> request)
            => new Selected<List<PlacedObjectEntry<T>>>(await ChoosePlacements(request));

        private Task<List<PlacedObjectEntry<T>>> ChoosePlacements(PlaceObjectsRequest<T> request)
        {
            var zone = request.DeploymentZone;
            ZoneBounds bounds = zone.Bounds;
            _impassibleTerrain = _tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();
            float minEnemyDist = request.MinDistanceFromEnemiesInches;
            var enemies = minEnemyDist > 0f
                ? GetEnemyPositions(request.TargetPlayerID)
                : new List<Position>();

            _deployCountPerPlayer.TryGetValue(request.TargetPlayerID, out int deployIndex);
            _deployCountPerPlayer[request.TargetPlayerID] = deployIndex + 1;

            var models = request.ModelsToPlace;
            if (models.Count == 0)
                return Task.FromResult(new List<PlacedObjectEntry<T>>());

            // maxRadius (circumscribing) is the rotation-safe bound for the zone margins / on-table containment.
            float maxRadius = models.Select(b => GetBaseRadius(b.GetValue())).DefaultIfEmpty(0.75f).Max();

            // Per-axis cell spacing (#150): a single radius over-spaces the short axis of an elongated rectangle
            // (breaking the 1" cohesion rule) or under-spaces the long axis (overlapping). Extents are measured at
            // the deploy facing (axis-aligned toward the table centre), matching CohesiveFormation.PackGrid so
            // deploy and movement form the same cohesive shape. A circle gives (r, r) — the old square grid.
            Float2 deployFacing = PlacementUtilities.DefaultDeployFacing(bounds, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            float maxHalfX = 0.1f, maxHalfZ = 0.1f;
            foreach (var b in models)
            {
                var (hx, hz) = GetHalfExtents(b.GetValue(), deployFacing);
                if (hx > maxHalfX) maxHalfX = hx;
                if (hz > maxHalfZ) maxHalfZ = hz;
            }
            float spacingX = 2f * maxHalfX + 0.1f; // 0.1" base-to-base per axis, so adjacent models satisfy the 1" rule
            float spacingZ = 2f * maxHalfZ + 0.1f;

            int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(models.Count)));
            int rows = (int)MathF.Ceiling(models.Count / (float)cols);
            float gridWidth = (cols - 1) * spacingX;
            float gridHeight = (rows - 1) * spacingZ;

            var existing = GetTableOccupants().ToList();

            // Preferred block centre: this unit's fan-out lane (across the zone width) + alternating Z band.
            float usableLeft = bounds.Left + maxRadius;
            float usableWidth = MathF.Max(0f, (bounds.Right - maxRadius) - usableLeft);
            float laneStep = MathF.Max(spacingX, FanOutLaneSpacingInches);
            int lanes = Math.Max(1, (int)(usableWidth / laneStep));
            int lane = deployIndex % lanes;
            int band = deployIndex / lanes;
            int signedBand = (band % 2 == 0) ? band / 2 : -((band + 1) / 2); // 0, +1, -1, +2, -2, ...

            float preferredCx = usableLeft + lane * laneStep + gridWidth / 2f;
            float preferredCz = bounds.CenterZ + signedBand * FanOutBandSpacingInches;

            // Range of block centres that keep every cell inside the zone bounds. If the block is wider/taller
            // than the usable zone the range inverts; the clamps below then collapse it to the zone centre.
            float cxMin = usableLeft + gridWidth / 2f;
            float cxMax = (bounds.Right - maxRadius) - gridWidth / 2f;
            float czMin = (bounds.Bottom + maxRadius) + gridHeight / 2f;
            float czMax = (bounds.Top - maxRadius) - gridHeight / 2f;

            Position center;
            Float2? facing = null;
            if (request.MustTouchTableEdge)
            {
                // #029 edge redeploy: the block must come back on touching a table edge.
                center = FindEdgeBlockCenter(zone, maxRadius, cols, spacingX, spacingZ, gridWidth, gridHeight,
                    models.Count, preferredCx, preferredCz, cxMin, cxMax, czMin, czMax, existing, enemies,
                    minEnemyDist, out facing);
            }
            else
            {
                center = FindBlockCenter(zone, maxRadius, cols, spacingX, spacingZ, gridWidth, gridHeight,
                    models.Count, preferredCx, preferredCz, cxMin, cxMax, czMin, czMax, existing, enemies, minEnemyDist);
                // Face toward the table centre (a top-zone unit faces down) — matters for Aircraft, whose
                // heading IS this facing (#150); a whole-table zone (Ambush) lands on the neutral +Z default.
                facing = PlacementUtilities.DefaultDeployFacing(bounds, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            }

            var positions = BuildGrid(center, cols, spacingX, spacingZ, gridWidth, gridHeight, models.Count);
            var placed = new List<PlacedObjectEntry<T>>(models.Count);
            for (int i = 0; i < models.Count; i++)
            {
                // Last-resort net: if a clear block never fit (crowded zone, or a unit bigger than its zone) the
                // fallback centre can push edge models past the bounds — clamp each base so it never lands off the
                // table. Normal placements already sit inside the zone, so this is a no-op for them.
                Position safe = PlacementUtilities.ClampBaseWithinTable(positions[i], GetBaseRadius(models[i].GetValue()),
                    GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
                placed.Add(new PlacedObjectEntry<T>(models[i], safe, facing));
            }

            return Task.FromResult(placed);
        }

        // The model positions of a block centred at (center): cols per row, filling left→right, top→bottom,
        // for exactly `count` models (the last row may be partial). Per-axis spacing so an elongated rectangle
        // packs tight on its short axis and clear on its long axis (#150).
        private static List<Position> BuildGrid(Position center, int cols, float spacingX, float spacingZ,
            float gridWidth, float gridHeight, int count)
        {
            var positions = new List<Position>(count);
            for (int k = 0; k < count; k++)
            {
                int col = k % cols, row = k / cols;
                positions.Add(new Position(
                    center.x - gridWidth / 2f + col * spacingX,
                    center.z - gridHeight / 2f + row * spacingZ));
            }
            return positions;
        }

        // Searches block centres (preferred lane/band first, then spiralling outward in `spacing` steps within
        // the in-bounds range) for one where the whole block is legal. Falls back to the clamped preferred
        // centre — block intact, so the unit is cohesion-valid even when the zone is too cramped to be clear.
        private Position FindBlockCenter(IBoundedZone zone, float radius, int cols, float spacingX, float spacingZ,
            float gridWidth, float gridHeight, int count, float preferredCx, float preferredCz,
            float cxMin, float cxMax, float czMin, float czMax,
            List<(Position pos, float radius)> existing, List<Position> enemies, float minEnemyDist)
        {
            float startCx = Math.Clamp(preferredCx, MathF.Min(cxMin, cxMax), MathF.Max(cxMin, cxMax));
            float startCz = Math.Clamp(preferredCz, MathF.Min(czMin, czMax), MathF.Max(czMin, czMax));

            foreach (float cz in CandidateAxis(startCz, czMin, czMax, spacingZ))
                foreach (float cx in CandidateAxis(startCx, cxMin, cxMax, spacingX))
                {
                    var center = new Position(cx, cz);
                    if (BlockIsValid(center, zone, radius, cols, spacingX, spacingZ, gridWidth, gridHeight, count, existing, enemies, minEnemyDist))
                        return center;
                }

            return new Position(startCx, startCz);
        }

        // Values from `start` outward (start, +step, -step, +2step, …), clamped to [min,max]; if the range is
        // empty (block bigger than the zone) yields the midpoint once so the block still gets placed.
        private static IEnumerable<float> CandidateAxis(float start, float min, float max, float step)
        {
            if (min > max) { yield return (min + max) / 2f; yield break; }
            var seen = new HashSet<int>();
            for (int i = 0; ; i++)
            {
                float v = start + ((i % 2 == 0) ? 1 : -1) * ((i + 1) / 2) * step;
                if (v < min) v = min;
                else if (v > max) v = max;
                int key = (int)MathF.Round(v * 100f);
                if (seen.Add(key)) yield return v;
                // Stop once we've swept both ends.
                if (start - ((i + 1) / 2) * step <= min && start + ((i + 1) / 2) * step >= max) break;
            }
        }

        // #029 edge mode: one axis is pinned to its in-bounds extreme, so the block's outermost bases sit
        // exactly `radius` from that zone edge — touching it. The other axis sweeps for a clear spot. Edges
        // are tried nearest-to-preferred first; models face inward from the chosen edge, so a redeployed
        // Aircraft's new heading (facing = heading, #150) doesn't immediately carry it back off the table.
        private Position FindEdgeBlockCenter(IBoundedZone zone, float radius, int cols, float spacingX, float spacingZ,
            float gridWidth, float gridHeight, int count, float preferredCx, float preferredCz,
            float cxMin, float cxMax, float czMin, float czMax,
            List<(Position pos, float radius)> existing, List<Position> enemies, float minEnemyDist,
            out Float2? inwardFacing)
        {
            var edges = new (float dist, float exMin, float exMax, float ezMin, float ezMax, Float2 facing)[]
            {
                (MathF.Abs(preferredCz - czMin), cxMin, cxMax, czMin, czMin, new Float2(0f, 1f)),   // bottom → face +Z
                (MathF.Abs(preferredCz - czMax), cxMin, cxMax, czMax, czMax, new Float2(0f, -1f)),  // top → face −Z
                (MathF.Abs(preferredCx - cxMin), cxMin, cxMin, czMin, czMax, new Float2(1f, 0f)),   // left → face +X
                (MathF.Abs(preferredCx - cxMax), cxMax, cxMax, czMin, czMax, new Float2(-1f, 0f)),  // right → face −X
            };

            foreach (var edge in edges.OrderBy(e => e.dist))
            {
                float startCx = Math.Clamp(preferredCx, MathF.Min(edge.exMin, edge.exMax), MathF.Max(edge.exMin, edge.exMax));
                float startCz = Math.Clamp(preferredCz, MathF.Min(edge.ezMin, edge.ezMax), MathF.Max(edge.ezMin, edge.ezMax));
                foreach (float cz in CandidateAxis(startCz, edge.ezMin, edge.ezMax, spacingZ))
                    foreach (float cx in CandidateAxis(startCx, edge.exMin, edge.exMax, spacingX))
                    {
                        var centre = new Position(cx, cz);
                        if (BlockIsValid(centre, zone, radius, cols, spacingX, spacingZ, gridWidth, gridHeight, count, existing, enemies, minEnemyDist))
                        {
                            inwardFacing = edge.facing;
                            return centre;
                        }
                    }
            }

            // No clear spot on any edge — intact block on the bottom edge at the clamped preferred lane
            // (cramped but touching and never scattered, matching the non-edge fallback's philosophy).
            inwardFacing = new Float2(0f, 1f);
            return new Position(
                Math.Clamp(preferredCx, MathF.Min(cxMin, cxMax), MathF.Max(cxMin, cxMax)),
                czMin);
        }

        private bool BlockIsValid(Position center, IBoundedZone zone, float radius, int cols, float spacingX, float spacingZ,
            float gridWidth, float gridHeight, int count, List<(Position pos, float radius)> existing,
            List<Position> enemies, float minEnemyDist)
        {
            foreach (Position p in BuildGrid(center, cols, spacingX, spacingZ, gridWidth, gridHeight, count))
            {
                // The whole base must fit in the zone (footprint, not just the centre) — so no base pokes past a
                // zone edge, and since deployment zones are flush with the table edge, none goes off the table.
                if (!PlacementUtilities.IsBaseWithinZone(p, radius, zone)) return false;
                if (OverlapsExisting(p, radius, existing)) return false;
                if (PlacementUtilities.OverlapsImpassibleTerrain(p, radius, _impassibleTerrain)) return false;
                if (TooCloseToEnemy(p, enemies, minEnemyDist)) return false;
            }
            return true;
        }

        private List<Position> GetEnemyPositions(PlayerID self)
        {
            var positions = new List<Position>();
            foreach (var unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID == self) continue;
                foreach (var model in unit.Models)
                {
                    if (model is ModelData md && md.GetIsAlive() && (md.Position.x != 0f || md.Position.z != 0f))
                        positions.Add(md.Position);
                }
            }
            return positions;
        }

        private static bool TooCloseToEnemy(Position p, List<Position> enemies, float minDist)
        {
            foreach (var e in enemies)
                if (Dist(p, e) < minDist) return true;
            return false;
        }

        private IEnumerable<(Position, float)> GetTableOccupants()
        {
            // A model belonging to an off-table unit occupies nothing; ask the unit rather than trusting
            // that its parked coordinate is exactly the origin.
            var offTable = new HashSet<IModel>(ReferenceEqualityComparer.Instance);
            foreach (IUnit unit in _tableState.Units.Objects)
            {
                if (unit.GetIsOnBattlefield()) continue;
                foreach (IModel m in unit.Models) offTable.Add(m);
            }

            foreach (var model in _tableState.Models.Objects)
            {
                if (offTable.Contains(model)) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;
                // Circumscribing radius so the placing block keeps clear of an existing rectangular base at any
                // facing (consistent with the placing model's radius). A conservative cross-unit LAYOUT bound —
                // the block's own cohesion is guaranteed by its per-axis grid, and exact shape-vs-shape collision
                // is enforced by the movement validators (#150).
                yield return (pos, model.BaseShape.CircumscribedRadiusInches);
            }
        }

        private static bool OverlapsExisting(Position p, float r, List<(Position pos, float radius)> existing)
        {
            foreach (var (ep, er) in existing)
                if (Dist(p, ep) < r + er) return true;
            return false;
        }

        private static float Dist(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        // The AI auto-placer lays out an axis-aligned grid using each model's CIRCUMSCRIBING circle — a
        // conservative bound that never overlaps or leaves the zone for any base shape or facing, and matches
        // the GUI deploy resolver's convention (#150). The inscribed BaseRadiusInches under-bounds a
        // rectangular base and let adjacent bases overlap. This is a layout bound only; exact shape-vs-shape
        // collision (BaseShapeGeometry) is what the movement validators and manual deploy enforce.
        private static float GetBaseRadius(T value) =>
            value is ModelData m ? m.BaseShape.CircumscribedRadiusInches : 0.75f;

        // Per-axis footprint half-extents at the deploy facing, for the grid's column/row spacing (#150).
        // A rectangle packs tight on its short axis and clear on its long axis, so the block satisfies both
        // cohesion (1" nearest-neighbour) and overlap-free — a single radius can't do both for a non-square base.
        private static (float hx, float hz) GetHalfExtents(T value, Float2 facing) =>
            value is ModelData m ? BaseShapeGeometry.FootprintHalfExtents(m.BaseShape, facing) : (0.75f, 0.75f);
    }
}
