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
        /// (<paramref name="centerX"/>, <paramref name="centerZ"/>), each model assigned to its nearest
        /// free slot to keep per-model moves small.
        /// </summary>
        public static List<ModelMoveEntry> PackGrid(IReadOnlyList<DataBinding<ModelData>> models, float centerX, float centerZ)
        {
            if (models.Count == 0) return new List<ModelMoveEntry>();
            if (models.Count == 1)
                return new List<ModelMoveEntry> { new ModelMoveEntry(models[0], new List<Position> { new Position(centerX, centerZ) }) };

            float spacing = GridSpacing(models);
            int cols = (int)MathF.Ceiling(MathF.Sqrt(models.Count));
            int rows = (int)MathF.Ceiling(models.Count / (float)cols);
            float gridWidth = (cols - 1) * spacing;
            float gridHeight = (rows - 1) * spacing;

            var slots = new List<Position>(models.Count);
            for (int k = 0; k < models.Count; k++)
            {
                int col = k % cols, row = k / cols;
                slots.Add(new Position(centerX - gridWidth / 2f + col * spacing,
                                       centerZ - gridHeight / 2f + row * spacing));
            }
            return AssignNearest(models, slots);
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

            float spacing = GridSpacing(models);
            int cols = (int)MathF.Ceiling(MathF.Sqrt(models.Count));
            int rows = (int)MathF.Ceiling(models.Count / (float)cols);
            float gridRadius = 0.5f * spacing * MathF.Sqrt((cols - 1) * (cols - 1) + (rows - 1) * (rows - 1));
            float currentSpread = models.Max(mb =>
            {
                var p = mb.GetValue().Position;
                return MathF.Sqrt((p.x - centroidX) * (p.x - centroidX) + (p.z - centroidZ) * (p.z - centroidZ));
            });
            float maxStep = Math.Max(0f, moveBudget - currentSpread - gridRadius - 0.05f);
            return Math.Min(Math.Max(0f, desiredStep), maxStep);
        }

        private static float GridSpacing(IReadOnlyList<DataBinding<ModelData>> models)
            => 2f * models.Max(mb => mb.GetValue().BaseRadiusInches) + 0.1f; // 0.1" base-to-base

        private static List<ModelMoveEntry> AssignNearest(IReadOnlyList<DataBinding<ModelData>> models, List<Position> slots)
        {
            var freeSlots = new List<Position>(slots);
            var entries = new List<ModelMoveEntry>(models.Count);
            foreach (var mb in models)
            {
                var p = mb.GetValue().Position;
                int best = 0;
                float bestDist = float.MaxValue;
                for (int i = 0; i < freeSlots.Count; i++)
                {
                    float d = (p.x - freeSlots[i].x) * (p.x - freeSlots[i].x) + (p.z - freeSlots[i].z) * (p.z - freeSlots[i].z);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                entries.Add(new ModelMoveEntry(mb, new List<Position> { freeSlots[best] }));
                freeSlots.RemoveAt(best);
            }
            return entries;
        }
    }
}
