using System;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
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

        // Living models of one Aircraft must share a heading to within this tolerance (they move rigidly and are
        // aimed together, so any larger divergence is a bug, not float drift).
        private const float HeadingMatchToleranceInches = 0.01f;

        /// <summary>
        /// The Aircraft's flight heading (a unit vector), held on every living model's <see cref="IModel.Facing"/>
        /// (#150). Set lazily on first use: if the unit isn't yet aimed (no <see cref="TokenType.AircraftHeadingSet"/>
        /// token), point it from the living-model centroid toward the table centre, store it on every living model,
        /// and mark the unit aimed ("set at deployment, never turned"). Once aimed, read it back from the models —
        /// asserting they share one heading — and never recompute, until the unit flies off and the token is cleared.
        /// </summary>
        public static Float2 EnsureHeading(UnitData unit)
        {
            if (unit.Tokens.HasToken(TokenType.AircraftHeadingSet))
                return GetSharedHeading(unit);

            Float2 heading;
            if (!TryGetCentroid(unit, out float cx, out float cz))
            {
                heading = new Float2(0f, 1f);
            }
            else
            {
                float tx = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * 0.5f;
                float tz = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES * 0.5f;
                float dx = tx - cx, dz = tz - cz;
                float length = MathF.Sqrt(dx * dx + dz * dz);
                heading = length > 0.001f ? new Float2(dx / length, dz / length) : new Float2(0f, 1f);
            }

            foreach (DataBinding<ModelData> model in unit.ModelBindings)
            {
                if (!model.GetValue().GetIsAlive()) continue;
                model.GetValue().SetFacing(heading);
            }
            unit.Tokens.AddToken(new Token(TokenType.AircraftHeadingSet, 1, new TokenClearTrigger.ManualOnly()));
            return heading;
        }

        // Reads the shared heading back from the unit's living models, asserting they all face the same way (an
        // Aircraft's models never turn independently). Falls back to +Z if the unit has no living models.
        private static Float2 GetSharedHeading(UnitData unit)
        {
            Float2? shared = null;
            foreach (DataBinding<ModelData> model in unit.ModelBindings)
            {
                if (!model.GetValue().GetIsAlive()) continue;
                Float2 facing = model.GetValue().Facing;
                if (shared is not Float2 s)
                {
                    shared = facing;
                    continue;
                }
                if (MathF.Abs(s.X - facing.X) > HeadingMatchToleranceInches ||
                    MathF.Abs(s.Y - facing.Y) > HeadingMatchToleranceInches)
                {
                    throw new InvalidOperationException(
                        $"Aircraft '{unit.Name}' has models with divergent facing — an Aircraft's models must share one heading.");
                }
            }
            return shared ?? new Float2(0f, 1f);
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
