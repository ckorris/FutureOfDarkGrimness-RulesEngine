using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Arranges a unit's living models into a tight, cohesion-valid grid for movement. Casualties leave
    /// holes in a formation (the neighbours of a dead model end up &gt;1" apart), so a rigid translate of
    /// the survivors would be rejected for breaking cohesion — re-packing into a fresh grid (0.1"
    /// base-to-base) always satisfies the 1" rule. Shared by the AI and CLI movement resolvers so a unit
    /// that has taken casualties can still produce a legal move.
    /// </summary>
    public static class CohesiveFormation
    {
        /// <summary>
        /// Move entries that pack <paramref name="models"/> into a tight grid centred at
        /// (<paramref name="centerX"/>, <paramref name="centerZ"/>). Within each row the models sit
        /// edge-to-edge (0.1" base-to-base) at their OWN base size, and rows are stacked by their tallest
        /// model — so a unit mixing large and small bases still satisfies the 1" nearest-neighbour rule
        /// (a uniform max-base spacing stranded small models &gt;1" from every neighbour, crashing the AI
        /// movement path validation — #159). Cells are assigned to keep per-model moves small.
        /// </summary>
        public static List<ModelMoveEntry> PackGrid(IReadOnlyList<DataBinding<ModelData>> models, float centerX, float centerZ)
        {
            if (models.Count == 0) return new List<ModelMoveEntry>();
            if (models.Count == 1)
                return new List<ModelMoveEntry> { new ModelMoveEntry(models[0], new List<Position> { new Position(centerX, centerZ) }) };

            int n = models.Count;
            int cols = (int)MathF.Ceiling(MathF.Sqrt(n));
            // Never leave a single model alone in the short last row: with no in-row neighbour it would have to
            // lean on a cross-row neighbour for the 1" rule, which varied base sizes can't guarantee (#159).
            // Bumping cols so the remainder is 0 or >=2 keeps every row at >=2 models, each 0.1" from its
            // in-row neighbour — the nearest-neighbour rule then holds for any base-size mix.
            if (n % cols == 1) cols++;
            int rows = (int)MathF.Ceiling(n / (float)cols);

            // Assign each model to a row-major cell (nearest coarse-grid slot), keeping the packing close to
            // the models' current relative layout so per-model moves stay small. The coarse grid only fixes
            // the arrangement; actual positions are recomputed with per-model spacing in LayoutOffsets (#277).
            DataBinding<ModelData>[] cellModels = AssignModelsToCells(models, cols, rows, centerX, centerZ);

            var rowCounts = new int[rows];
            for (int row = 0; row < rows; row++) rowCounts[row] = Math.Min(cols, n - row * cols);

            var halfXs = new float[n];
            var halfZs = new float[n];
            for (int k = 0; k < n; k++)
                (halfXs[k], halfZs[k]) = BaseShapeGeometry.FootprintHalfExtents(
                    cellModels[k].GetValue().BaseShape, cellModels[k].GetValue().Facing);

            var offsets = FormationLibrary.LayoutOffsets(halfXs, halfZs, rowCounts, gap: 0.1f);
            var entries = new List<ModelMoveEntry>(n);
            for (int k = 0; k < n; k++)
                entries.Add(new ModelMoveEntry(cellModels[k],
                    new List<Position> { new Position(centerX + offsets[k].dx, centerZ + offsets[k].dz) }));
            return entries;
        }

        /// <summary>
        /// Whether every living model is currently within the 1" nearest-neighbour and 9" all-models coherency
        /// limits — i.e. the unit is NOT broken. A mid-unit casualty leaves survivors &gt;1" apart, which this
        /// reports as not-cohesive so the consolidation resolvers know to re-form instead of a rigid translate
        /// (#159). 0-1 models are trivially cohesive.
        /// </summary>
        public static bool IsCohesive(IReadOnlyList<DataBinding<ModelData>> living)
        {
            if (living.Count <= 1) return true;

            const float epsilon = 0.001f;
            for (int i = 0; i < living.Count; i++)
            {
                float nearest = float.PositiveInfinity, farthest = 0f;
                for (int j = 0; j < living.Count; j++)
                {
                    if (i == j) continue;
                    float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                        living[i].GetValue().Position, living[j].GetValue().Position,
                        living[i].GetValue().BaseShape, living[i].GetValue().Facing,
                        living[j].GetValue().BaseShape, living[j].GetValue().Facing);
                    nearest = MathF.Min(nearest, d);
                    farthest = MathF.Max(farthest, d);
                }
                if (nearest > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES + epsilon) return false;
                if (farthest > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES + epsilon) return false;
            }
            return true;
        }

        /// <summary>
        /// Best-effort consolidation re-form (#159): packs the living models into a tight cohesive grid centred
        /// at (<paramref name="centerX"/>, <paramref name="centerZ"/>), but caps each model's move at
        /// <paramref name="maxStepInches"/>. If the cap is generous the unit fully re-forms into coherency; if a
        /// casualty hole is too wide for the cap, every model still steps as far toward its slot as the cap
        /// allows — pulling the unit as close to coherency as the consolidation permits, and never worse (so it
        /// passes the not-worsened consolidation coherency check). Centre this on the current living centroid so
        /// no model is asked to move farther than necessary.
        /// </summary>
        public static List<ModelMoveEntry> ReformTowardWithinCap(IReadOnlyList<DataBinding<ModelData>> living,
            float centerX, float centerZ, float maxStepInches)
        {
            var packed = PackGrid(living, centerX, centerZ);
            var result = new List<ModelMoveEntry>(packed.Count);
            foreach (var entry in packed)
            {
                Position cur = entry.Model.GetValue().Position;
                Position slot = entry.Positions[entry.Positions.Count - 1];
                float dx = slot.x - cur.x, dz = slot.z - cur.z;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                Position dest = (d <= maxStepInches || d < 1e-6f)
                    ? slot
                    : new Position(cur.x + dx / d * maxStepInches, cur.z + dz / d * maxStepInches);
                result.Add(new ModelMoveEntry(entry.Model, new List<Position> { dest }));
            }
            return result;
        }

        /// <summary>
        /// Clamps an intended advance so that re-packing keeps every model within
        /// <paramref name="moveBudget"/>: the worst-case per-model move ≈ step + the model's current
        /// offset from the centroid + the grid radius.
        /// </summary>
        public static float ClampRepackStep(IReadOnlyList<DataBinding<ModelData>> models,
            float centroidX, float centroidZ, float desiredStep, float moveBudget)
        {
            if (models.Count <= 1) return Math.Max(0f, desiredStep);

            var (sx, sz) = GridSpacingXZ(models);
            int cols = (int)MathF.Ceiling(MathF.Sqrt(models.Count));
            int rows = (int)MathF.Ceiling(models.Count / (float)cols);
            float gw = (cols - 1) * sx, gh = (rows - 1) * sz;
            float gridRadius = 0.5f * MathF.Sqrt(gw * gw + gh * gh);
            float currentSpread = models.Max(mb =>
            {
                var p = mb.GetValue().Position;
                return MathF.Sqrt((p.x - centroidX) * (p.x - centroidX) + (p.z - centroidZ) * (p.z - centroidZ));
            });
            float maxStep = Math.Max(0f, moveBudget - currentSpread - gridRadius - 0.05f);
            return Math.Min(Math.Max(0f, desiredStep), maxStep);
        }

        // Per-axis grid spacing (#150): column spacing from the widest base's X extent, row spacing from the
        // tallest base's Z extent, each + a 0.1" base-to-base gap. A single radius can't pack a non-square
        // rectangle both overlap-free AND within the 1" cohesion rule — the short axis needs the smaller
        // spacing. Facing-aware via the footprint extents; a circle gives (r, r), reproducing the old square grid.
        private static (float x, float z) GridSpacingXZ(IReadOnlyList<DataBinding<ModelData>> models)
        {
            float maxHalfX = 0f, maxHalfZ = 0f;
            foreach (var mb in models)
            {
                var m = mb.GetValue();
                var (hx, hz) = BaseShapeGeometry.FootprintHalfExtents(m.BaseShape, m.Facing);
                if (hx > maxHalfX) maxHalfX = hx;
                if (hz > maxHalfZ) maxHalfZ = hz;
            }
            return (2f * maxHalfX + 0.1f, 2f * maxHalfZ + 0.1f);
        }

        // Maps each row-major cell (index row*cols+col, only the first models.Count populated) to a model,
        // greedily choosing the nearest not-yet-placed model to a coarse uniform grid slot. This decides only
        // the relative arrangement (who is top-left vs bottom-right) to keep per-model moves small; PackGrid
        // recomputes each cell's actual position with per-model spacing.
        private static DataBinding<ModelData>[] AssignModelsToCells(IReadOnlyList<DataBinding<ModelData>> models,
            int cols, int rows, float centerX, float centerZ)
        {
            int n = models.Count;
            var (sx, sz) = GridSpacingXZ(models);
            float gridWidth = (cols - 1) * sx, gridHeight = (rows - 1) * sz;

            var cellModels = new DataBinding<ModelData>[n];
            var used = new bool[n];
            for (int k = 0; k < n; k++)
            {
                int col = k % cols, row = k / cols;
                float slotX = centerX - gridWidth / 2f + col * sx;
                float slotZ = centerZ - gridHeight / 2f + row * sz;

                int best = -1;
                float bestDist = float.MaxValue;
                for (int m = 0; m < n; m++)
                {
                    if (used[m]) continue;
                    var p = models[m].GetValue().Position;
                    float d = (p.x - slotX) * (p.x - slotX) + (p.z - slotZ) * (p.z - slotZ);
                    if (d < bestDist) { bestDist = d; best = m; }
                }
                used[best] = true;
                cellModels[k] = models[best];
            }
            return cellModels;
        }
    }
}
