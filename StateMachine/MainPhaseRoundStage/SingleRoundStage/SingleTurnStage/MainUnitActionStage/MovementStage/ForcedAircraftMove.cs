using System;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #029 — Aircraft forced movement. An Aircraft may only Advance, and that Advance is a forced straight-line
    /// 30–36" move along its fixed heading — the facing the player gave its models at deployment (#150); it never
    /// turns while on the table. This helper reads the heading and builds the rigid straight-line paths;
    /// DefinePathStage drives it and handles the "flew off the table" case (see <see cref="WouldLeaveTable"/>).
    /// </summary>
    public static class ForcedAircraftMove
    {
        /// <summary>The forced Advance's distance band — the unit flies 30–36" straight ahead, player's choice.</summary>
        public const float MinDistanceInches = 30f;
        public const float MaxDistanceInches = 36f;

        /// <summary>
        /// The Aircraft's flight heading (a unit vector): the shared <see cref="IModel.Facing"/> of its living
        /// models, exactly as the player placed it at deployment (or edge redeploy) — there is no auto-aim, and
        /// it never changes while the unit is on the table (#150). Read from the first living model.
        /// </summary>
        public static Float2 GetHeading(UnitData unit)
        {
            // The first living model's facing is the unit's heading. Its models should all share it, but
            // single-model deployment can set them apart (no bundled book has a multi-model Aircraft, so it
            // takes a hand-built list). That used to throw here on every Advance, ending the game; flying the
            // whole unit on the lead model's heading keeps it rigid and coherent instead.
            foreach (DataBinding<ModelData> model in unit.ModelBindings)
            {
                if (model.GetValue().GetIsAlive()) return model.GetValue().Facing;
            }
            return new Float2(0f, 1f);
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
        /// would land outside [r, W−r] × [r, H−r], with r the CIRCUMSCRIBING radius so a rotated rectangular
        /// base is judged at any heading (#150). That's the "flew off the edge" signal (the unit then leaves
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
                float r = move.Model.GetValue().BaseShape.CircumscribedRadiusInches;
                if (end.x < r || end.x > w - r || end.z < r || end.z > h - r)
                    return true;
            }
            return false;
        }

    }
}
