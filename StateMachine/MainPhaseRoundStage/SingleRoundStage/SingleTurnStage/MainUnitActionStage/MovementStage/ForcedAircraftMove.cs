using System;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #029 — Aircraft forced movement. An Aircraft may only Advance, and that Advance is a forced straight-line
    /// 30–36" move along its fixed heading (it never turns while on the table). This helper supplies the heading
    /// (set once, toward the table centre) and the rigid straight-line paths; DefinePathStage drives it and
    /// handles the "flew off the table" case (see <see cref="WouldLeaveTable"/>).
    /// </summary>
    public static class ForcedAircraftMove
    {
        /// <summary>The distances an Aircraft may choose for its forced Advance ("30–36\"").</summary>
        public static readonly float[] DistancesInches = { 30f, 33f, 36f };

        /// <summary>
        /// The Aircraft's flight heading (a unit vector), set lazily on first use: if unset, point it from the
        /// unit's living-model centroid toward the table centre and store it on the unit ("set at deployment,
        /// never turned"). Stays fixed thereafter until the unit flies off and is re-placed (which nulls it).
        /// </summary>
        public static Float2 EnsureHeading(UnitData unit)
        {
            if (unit.AircraftHeading is Float2 existing && (existing.X != 0f || existing.Y != 0f))
                return existing;

            if (!TryGetCentroid(unit, out float cx, out float cz))
            {
                Float2 fallback = new Float2(0f, 1f);
                unit.AircraftHeading = fallback;
                return fallback;
            }

            float tx = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * 0.5f;
            float tz = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES * 0.5f;
            float dx = tx - cx, dz = tz - cz;
            float length = MathF.Sqrt(dx * dx + dz * dz);

            Float2 heading = length > 0.001f ? new Float2(dx / length, dz / length) : new Float2(0f, 1f);
            unit.AircraftHeading = heading;
            return heading;
        }

        /// <summary>
        /// The rigid straight-line move: every living model translates by <paramref name="heading"/> ×
        /// <paramref name="distanceInches"/>. The whole unit shifts by the same delta, so coherency is preserved.
        /// </summary>
        public static List<ModelMoveEntry> BuildPaths(DataBinding<UnitData> unit, Float2 heading, float distanceInches)
        {
            float dx = heading.X * distanceInches;
            float dz = heading.Y * distanceInches;

            List<ModelMoveEntry> paths = new List<ModelMoveEntry>();
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                if (!model.GetIsAlive()) continue;
                Position from = model.GetValue().PositionBinding.GetValue();
                paths.Add(new ModelMoveEntry(model, new List<Position> { new Position(from.x + dx, from.z + dz) }));
            }
            return paths;
        }

        /// <summary>
        /// Whether the move <paramref name="paths"/> would carry any model's base off the table — its centre
        /// would land outside [r, W−r] × [r, H−r]. That's the "flew off the edge" signal (the unit then leaves
        /// play and redeploys from an edge next round) rather than a move to commit.
        /// </summary>
        public static bool WouldLeaveTable(List<ModelMoveEntry> paths)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

            foreach (ModelMoveEntry move in paths)
            {
                if (move.Positions.Count == 0) continue;
                Position end = move.Positions[move.Positions.Count - 1];
                float r = move.Model.GetValue().BaseRadiusInches;
                if (end.x < r || end.x > w - r || end.z < r || end.z > h - r)
                    return true;
            }
            return false;
        }

        private static bool TryGetCentroid(UnitData unit, out float cx, out float cz)
        {
            float sx = 0f, sz = 0f;
            int n = 0;
            foreach (DataBinding<ModelData> model in unit.ModelBindings)
            {
                if (!model.GetIsAlive()) continue;
                Position p = model.GetValue().PositionBinding.GetValue();
                sx += p.x; sz += p.z; n++;
            }
            if (n == 0) { cx = cz = 0f; return false; }
            cx = sx / n; cz = sz / n;
            return true;
        }
    }
}
